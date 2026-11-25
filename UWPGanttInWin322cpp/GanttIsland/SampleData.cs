using System;
using System.Collections.ObjectModel;
using Syncfusion.UI.Xaml.Gantt;

namespace GanttIsland
{
    // ---------------------------------------------------------------------------------------------
    // STEP 1 OF THE WORKFLOW - CREATING THE DATA
    //
    // SfGantt does not read a database or a file. It displays whatever objects you give it, so the
    // first job is to build those objects. The control understands a ready-made model class,
    // Syncfusion.UI.Xaml.Gantt.TaskDetail, so we use it directly (no custom model class is needed).
    //
    // A TaskDetail has these important properties:
    //   ID           - unique text id. Other tasks refer to it (for example in Predecessors).
    //   Name         - the text shown in the grid and on the task bar.
    //   StartDate / FinishDate - where the task bar starts and ends on the timeline.
    //   Progress     - percentage complete (0 - 100), drawn as the darker part of the bar.
    //   Children     - sub tasks. A task that has children is shown as a "summary" task.
    //   Predecessors - dependency lines (a task that must finish/start before this one).
    //   Resources    - ids of the people assigned to the task (see CreateProjectResources).
    //
    // The hierarchy created below is:
    //
    //   Product Launch                 (level 1, root summary task)
    //     |- Phase 1 - Planning        (level 2, summary task)
    //     |     |- Define scope, Plan budget, Planning complete (milestone)
    //     |- Phase 2 - Design          ...
    //     |- Phase 3 - Development     ...
    //     |- Phase 4 - Testing         ...
    //     |- Phase 5 - Release         ...
    // ---------------------------------------------------------------------------------------------
    internal static class SampleData
    {
        // The year of every date used in this sample.
        private const int SampleYear = 2026;

        // Builds the list of people that can be assigned to tasks.
        // The ids ("1", "2", "3") are what a task stores in TaskDetail.Resources.
        // The Gantt receives this list through its ProjectResources property (see GanttPage).
        public static GanttResourceCollection CreateProjectResources()
        {
            return new GanttResourceCollection
            {
                new GanttResource { ID = "1", Name = "Project Manager" },
                new GanttResource { ID = "2", Name = "Developer" },
                new GanttResource { ID = "3", Name = "Tester" },
            };
        }

        // Builds the complete task hierarchy and returns the collection that is bound to the Gantt.
        //
        // Why ObservableCollection? It raises change notifications. After the Gantt is bound, adding,
        // removing or editing a task in this collection updates the chart automatically.
        //
        // The method works bottom-up:
        //   1. create the leaf tasks of each phase,
        //   2. wrap each group of leaf tasks in a phase (summary) task,
        //   3. add the dependencies and resource assignments,
        //   4. wrap the phases in the single root task and return it.
        public static ObservableCollection<TaskDetail> CreateProjectTasks()
        {
            // ---- 1. Leaf tasks, grouped by phase ----
            // A milestone is simply a task whose start date and finish date are the same day.
            ObservableCollection<TaskDetail> planningTasks = new ObservableCollection<TaskDetail>
            {
                CreateTask("3", "Define scope", CreateDate(11, 2), CreateDate(11, 4), 100),
                CreateTask("4", "Plan budget", CreateDate(11, 2), CreateDate(11, 6), 100),
                CreateTask("5", "Planning complete (Milestone)", CreateDate(11, 9), CreateDate(11, 9), 100),
            };

            ObservableCollection<TaskDetail> designTasks = new ObservableCollection<TaskDetail>
            {
                CreateTask("7", "UI design", CreateDate(11, 10), CreateDate(11, 13), 100),
                CreateTask("8", "Technical design", CreateDate(11, 10), CreateDate(11, 17), 100),
                CreateTask("9", "Design complete (Milestone)", CreateDate(11, 18), CreateDate(11, 18), 100),
            };

            ObservableCollection<TaskDetail> developmentTasks = new ObservableCollection<TaskDetail>
            {
                CreateTask("11", "Backend development", CreateDate(11, 19), CreateDate(12, 2), 20),
                CreateTask("12", "Frontend development", CreateDate(11, 19), CreateDate(11, 27), 30),
                CreateTask("13", "Code complete (Milestone)", CreateDate(12, 3), CreateDate(12, 3), 0),
            };

            ObservableCollection<TaskDetail> testingTasks = new ObservableCollection<TaskDetail>
            {
                CreateTask("15", "Integration testing", CreateDate(12, 4), CreateDate(12, 10), 0),
                CreateTask("16", "User acceptance testing", CreateDate(12, 11), CreateDate(12, 15), 0),
            };

            ObservableCollection<TaskDetail> releaseTasks = new ObservableCollection<TaskDetail>
            {
                CreateTask("18", "Deployment", CreateDate(12, 16), CreateDate(12, 17), 0),
                CreateTask("19", "Go live (Milestone)", CreateDate(12, 18), CreateDate(12, 18), 0),
            };

            // ---- 2. Phase (summary) tasks ----
            // A phase has no dates of its own. Assigning Children makes the Gantt calculate its start,
            // finish and progress from the tasks inside it.
            ObservableCollection<TaskDetail> phaseTasks = new ObservableCollection<TaskDetail>
            {
                new TaskDetail { ID = "2",  Name = "Phase 1 - Planning",    Children = planningTasks },
                new TaskDetail { ID = "6",  Name = "Phase 2 - Design",      Children = designTasks },
                new TaskDetail { ID = "10", Name = "Phase 3 - Development", Children = developmentTasks },
                new TaskDetail { ID = "14", Name = "Phase 4 - Testing",     Children = testingTasks },
                new TaskDetail { ID = "17", Name = "Phase 5 - Release",     Children = releaseTasks },
            };

            // ---- 3a. Dependencies (Finish-to-Start) ----
            // Reads as: "<task> can start only after <predecessor ids> finish".
            // The Gantt draws these as connector lines and uses them to highlight the critical path.
            AddFinishToStartPredecessors(planningTasks[2], "3", "4");          // Planning complete <- Define scope, Plan budget
            AddFinishToStartPredecessors(designTasks[0], "5");                 // UI design         <- Planning complete
            AddFinishToStartPredecessors(designTasks[1], "5");                 // Technical design  <- Planning complete
            AddFinishToStartPredecessors(designTasks[2], "7", "8");            // Design complete   <- UI design, Technical design
            AddFinishToStartPredecessors(developmentTasks[0], "9");            // Backend           <- Design complete
            AddFinishToStartPredecessors(developmentTasks[1], "9");            // Frontend          <- Design complete
            AddFinishToStartPredecessors(developmentTasks[2], "11", "12");     // Code complete     <- Backend, Frontend
            AddFinishToStartPredecessors(testingTasks[0], "13");               // Integration test  <- Code complete
            AddFinishToStartPredecessors(testingTasks[1], "15");               // UAT               <- Integration test
            AddFinishToStartPredecessors(releaseTasks[0], "16");               // Deployment        <- UAT
            AddFinishToStartPredecessors(releaseTasks[1], "18");               // Go live           <- Deployment

            // ---- 3b. Resource assignments ----
            // The ids must match the ids created in CreateProjectResources().
            const string projectManagerId = "1";
            const string developerId = "2";
            const string testerId = "3";

            planningTasks[0].Resources.Add(projectManagerId);       // Define scope
            planningTasks[1].Resources.Add(projectManagerId);       // Plan budget
            designTasks[0].Resources.Add(developerId);              // UI design
            designTasks[1].Resources.Add(developerId);              // Technical design
            developmentTasks[0].Resources.Add(developerId);         // Backend development
            developmentTasks[1].Resources.Add(developerId);         // Frontend development
            testingTasks[0].Resources.Add(testerId);                // Integration testing
            testingTasks[1].Resources.Add(testerId);                // User acceptance testing
            releaseTasks[0].Resources.Add(projectManagerId);        // Deployment

            // ---- 4. Root task ----
            // The Gantt is bound to the list of ROOT tasks only. The rest of the tree is reached
            // through each task's Children, so this list holds just one item.
            return new ObservableCollection<TaskDetail>
            {
                new TaskDetail { ID = "1", Name = "Product Launch", Children = phaseTasks },
            };
        }

        // Creates one task with the given id, name, dates and progress percentage.
        private static TaskDetail CreateTask(string taskId, string taskName, DateTime startDate, DateTime finishDate, double progressPercentage)
        {
            return new TaskDetail
            {
                ID = taskId,
                Name = taskName,
                StartDate = startDate,
                FinishDate = finishDate,
                Progress = progressPercentage,
            };
        }

        // Shorthand for "this month and day in the sample year".
        private static DateTime CreateDate(int month, int day)
        {
            return new DateTime(SampleYear, month, day);
        }

        // Adds one Finish-to-Start dependency to the task for every predecessor id given.
        private static void AddFinishToStartPredecessors(TaskDetail task, params string[] predecessorTaskIds)
        {
            foreach (string predecessorTaskId in predecessorTaskIds)
            {
                task.Predecessors.Add(new TaskRelationship
                {
                    ID = predecessorTaskId,
                    Relationship = Relationship.FinishToStart,
                });
            }
        }
    }
}
