using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics.X86;

// Be prepared for a laaaaarge amount of comments

// === Job (Process) Control Block ===
// As per suggested approach in pdf
class Job
{
    public int JobId { get; set; }                 // Unique identifier for the job (like a name or number)
    public int EntryTime { get; set; }             // When this job enters the system (in "time units")
    public int RunDuration { get; set; }           // How much CPU time this job needs to finish
    public int TimeLeft { get; set; }              // How much CPU time is still needed (decreases as job runs)
    public int FirstRun { get; set; } = -1;        // The first time this job actually starts running on the CPU
    public int CompletionTime { get; set; }        // When the job finishes all its work
    public int QueuePosition { get; set; } = 0;    // For MLFQ: Which queue the job is currently in

    // Constructor to set up a new job with its info
    public Job(int id, int entry, int duration)
    {
        JobId = id;
        EntryTime = entry;
        RunDuration = duration;
        TimeLeft = duration;
    }
}

interface ISchedulingPolicy
{
    void Execute(List<Job> jobList); // Runs the scheduling algorithm
    void ShowResults();  // Prints the performance metrics
}

class OwlTechSimulator
{
    static void Main()
    {
        Console.WriteLine("=== OwlTech CPU Scheduler Simulator ===");
        Console.Write("How many jobs to schedule? ");
        int jobCount = int.Parse(Console.ReadLine());

        // Collect job info from the user input
        var jobs = new List<Job>();
        for (int i = 0; i < jobCount; i++)
        {
            Console.WriteLine($"\nJob #{i + 1} details:");
            Console.Write("  Entry time: ");
            int entry = int.Parse(Console.ReadLine());
            Console.Write("  Run duration: ");
            int duration = int.Parse(Console.ReadLine());
            jobs.Add(new Job(i + 1, entry, duration));
        }

        // Choose which Alg to use
        Console.WriteLine("\nChoose scheduling method:");
        Console.WriteLine("  [A] Multi-Level Feedback Queue (MLFQ)");
        Console.WriteLine("  [B] Highest Response Ratio Next (HRRN)");
        Console.Write("Enter choice (A/B): ");
        string alg = Console.ReadLine().Trim().ToUpper();

        // Anything other than A activates option B (lazy)
        ISchedulingPolicy policy = (alg == "A") ? new FeedbackQueueScheduler() : new ResponseRatioScheduler();

        // Use the alg to run the simulator and produce results
        policy.Execute(jobs);
        policy.ShowResults();

        // Pray that we reach here (or pray that results are correct)
        Console.WriteLine("\nDone!. Press Enter to exit.");
        Console.ReadLine();
    }
}

// === Multi-Level Feedback Queue Scheduler ===
class FeedbackQueueScheduler : ISchedulingPolicy
{
    // Use three queues for jobs, each with a different "priority" and time slice:
    // Level 0: Fastest, short time slice (quantum)
    // Level 1: Medium, longer time slice
    // Level 2: Slowest, runs jobs to completion (First Come First Served)
    private readonly List<Queue<Job>> levels = new List<Queue<Job>>
    {
        new Queue<Job>(), // Level 0: RR, quantum 4
        new Queue<Job>(), // Level 1: RR, quantum 8
        new Queue<Job>()  // Level 2: FCFS (no quantum, just runs to finish)
    };
    private int[] quantums = { 4, 8, int.MaxValue }; // How long each queue lets a job run before moving it down
    private List<Job> finishedJobs = new List<Job>(); // Store the jobs after they finish

    // MLFQ scheduling algorithm
    public void Execute(List<Job> jobList)
    {
        int now = 0; // "Clock" to track current time
        int idle = 0; // How much time CPU spends idle (no jobs to run)
        int jobsDone = 0; // How many jobs have finished
        var jobs = jobList.OrderBy(j => j.EntryTime).ToList(); // Sort jobs by when they arrive
        int totalJobs = jobs.Count;

        // Need to track which jobs have entered the system so it isn't added twice
        var arrived = new HashSet<Job>();

        // Keep it running until all jobs are finished
        while (jobsDone < totalJobs)
        {
            // Move jobs that have arrived into the top queue (highest priority)
            foreach (var job in jobs.Where(j => j.EntryTime <= now && !arrived.Contains(j)))
            {
                levels[0].Enqueue(job);
                arrived.Add(job);
            }

            // Find the next job to run (look for the first non-empty queue)
            int lvl = levels.FindIndex(q => q.Count > 0);
            if (lvl == -1)
            {
                // If all queues are empty, CPU must be idle. Fast-forward to next job's arrival
                int nextArrival = jobs.Where(j => !arrived.Contains(j)).Select(j => j.EntryTime).DefaultIfEmpty(now + 1).Min();
                idle += (nextArrival - now);
                now = nextArrival;
                continue;
            }

            Job current = levels[lvl].Dequeue(); // Get the next job from the appropriate queue
            int slice = Math.Min(current.TimeLeft, quantums[lvl]); // How much time this job gets to run now

            // If this is the first time the job runs, record it
            if (current.FirstRun == -1)
            {
                current.FirstRun = now;
            }

                // Run the job for the time slice or until completion
                int timeAdvanced = 0;
            while (timeAdvanced < slice)
            {
                now++;
                current.TimeLeft--;
                timeAdvanced++;

                // Check for new arrivals during this time
                foreach (var job in jobs.Where(j => j.EntryTime == now && !arrived.Contains(j)))
                {
                    levels[0].Enqueue(job);
                    arrived.Add(job);
                }

                if (current.TimeLeft == 0)
                    break; // Out of time
            }

            if (current.TimeLeft == 0)
            {
                // Job is done, record its finish time
                current.CompletionTime = now;
                finishedJobs.Add(current);
                jobsDone++;
            }
            else
            {
                // Job didn't finish; move it to the next lower queue (if not already at the lowest)
                int nextLvl = Math.Min(lvl + 1, levels.Count - 1);
                levels[nextLvl].Enqueue(current);
            }
        }
    }

    public void ShowResults()
    {
        PrintMetrics(finishedJobs, "Multi-Level Feedback Queue");
    }

    // === Calculations and pretty output ===
    private void PrintMetrics(List<Job> jobs, string algName)
    {
        Console.WriteLine($"\n=== {algName} Results ===");
        Console.WriteLine("Job | Entry | Duration | Start | Finish | Wait | Turnaround | Response");
        double totalWait = 0, totalTurn = 0, totalResp = 0, totalRun = 0;
        int earliest = jobs.Min(j => j.EntryTime);
        int latest = jobs.Max(j => j.CompletionTime);

        foreach (var j in jobs.OrderBy(j => j.JobId))
        {
            int wait = j.CompletionTime - j.EntryTime - j.RunDuration; // Waiting Time: Time spent in the queue before running
            int turn = j.CompletionTime - j.EntryTime; // Turnaround Time: Total time from arrival to finish
            int resp = j.FirstRun - j.EntryTime; // Response Time (optional): Time from arrival to first run
            totalWait += wait;
            totalTurn += turn;
            totalResp += resp;
            totalRun += j.RunDuration;
            Console.WriteLine($"{j.JobId,3} | {j.EntryTime,5} | {j.RunDuration,8} | {j.FirstRun,5} | {j.CompletionTime,6} | {wait,4} | {turn,10} | {resp,8}");
        }

        int n = jobs.Count;
        double makespan = latest - earliest; // Makespan: Total time from first arrival to last finish

        // Average Waiting Time: Mean time jobs spent waiting before first CPU access
        Console.WriteLine($"\nAverage Wait Time: {totalWait / n:F2}");
        // Average Turnaround Time: Mean time from entry to completion
        Console.WriteLine($"Average Turnaround Time: {totalTurn / n:F2}");
        // Average Response Time: Mean time from entry to first CPU access
        Console.WriteLine($"Average Response Time: {totalResp / n:F2}");
        // CPU Utilization: Fraction of time CPU was busy
        Console.WriteLine($"CPU Utilization: {100.0 * totalRun / makespan:F2}%");
        // Throughput: Number of jobs completed per unit time
        Console.WriteLine($"Throughput: {n / makespan:F2} jobs/unit time");
    }
}

// === Highest Response Ratio Next Scheduler ===
class ResponseRatioScheduler : ISchedulingPolicy
{
    private List<Job> completed = new List<Job>();
    // Runs the HRRN scheduling algorithm
    public void Execute(List<Job> jobList)
    {
        int now = 0;
        var jobs = jobList.OrderBy(j => j.EntryTime).ToList();
        var ready = new List<Job>();
        int totalJobs = jobs.Count;

        while (completed.Count < totalJobs)
        {
            // Add jobs that have arrived to the ready list
            ready.AddRange(jobs.Where(j => j.EntryTime <= now && !completed.Contains(j) && !ready.Contains(j)));

            if (ready.Count == 0)
            {
                // If no jobs are ready, fast-forward to next arrival
                int nextTime = jobs.Where(j => !completed.Contains(j)).Select(j => j.EntryTime).DefaultIfEmpty(now + 1).Min();
                now = nextTime;
                continue;
            }

            // HRRN: Choose the job with the highest "response ratio"
            Job next = ready
                .OrderByDescending(j =>
                {
                    // Favors jobs that have waited a long time and jobs with short durations
                    double wait = now - j.EntryTime;
                    double ratio = (wait + j.RunDuration) / (double)j.RunDuration; // // Response Ratio = (Waiting Time + Run Duration) / Run Duration
                    return ratio;
                })
                .First();

            ready.Remove(next);
            if (next.FirstRun == -1)
                next.FirstRun = now; // Record when job first starts running

            now += next.RunDuration; // Run job to completion (remember, not preemptive)
            next.TimeLeft = 0;
            next.CompletionTime = now;
            completed.Add(next);
        }
    }

    public void ShowResults()
    {
        PrintMetrics(completed, "Highest Response Ratio Next");
    }

    // === More Calculations and very pretty output ===
    private void PrintMetrics(List<Job> jobs, string algName)
    {
        Console.WriteLine($"\n=== {algName} Results ===");
        Console.WriteLine("Job | Entry | Duration | Start | Finish | Wait | Turnaround | Response");
        double totalWait = 0, totalTurn = 0, totalResp = 0, totalRun = 0;
        int earliest = jobs.Min(j => j.EntryTime);
        int latest = jobs.Max(j => j.CompletionTime);

        foreach (var j in jobs.OrderBy(j => j.JobId))
        {
            int wait = j.FirstRun - j.EntryTime;
            int turn = j.CompletionTime - j.EntryTime;
            int resp = j.FirstRun - j.EntryTime;
            totalWait += wait;
            totalTurn += turn;
            totalResp += resp;
            totalRun += j.RunDuration;
            Console.WriteLine($"{j.JobId,3} | {j.EntryTime,5} | {j.RunDuration,8} | {j.FirstRun,5} | {j.CompletionTime,6} | {wait,4} | {turn,10} | {resp,8}");
        }

        int n = jobs.Count;
        double makespan = latest - earliest;

        // Average Waiting Time: Mean time jobs spent waiting before first CPU access
        Console.WriteLine($"\nAverage Wait Time: {totalWait / n:F2}");
        // Average Turnaround Time: Mean time from entry to completion
        Console.WriteLine($"Average Turnaround Time: {totalTurn / n:F2}");
        // Average Response Time: Mean time from entry to first CPU access
        Console.WriteLine($"Average Response Time: {totalResp / n:F2}");
        // CPU Utilization: Fraction of time CPU was busy
        Console.WriteLine($"CPU Utilization: {100.0 * totalRun / makespan:F2}%");
        // Throughput: Number of jobs completed per unit time
        Console.WriteLine($"Throughput: {n / makespan:F2} jobs/unit time");
    }
}
