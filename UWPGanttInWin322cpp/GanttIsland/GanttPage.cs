using System;
using System.Collections.ObjectModel;
using System.Text;
using Syncfusion.UI.Xaml.Gantt;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

// Both Syncfusion.UI.Xaml.Gantt and Windows.UI.Xaml.Controls define these names; pick the Gantt ones.
using GanttSelectionChangedEventArgs = Syncfusion.UI.Xaml.Gantt.SelectionChangedEventArgs;
using GanttSelectionChangingEventArgs = Syncfusion.UI.Xaml.Gantt.SelectionChangingEventArgs;

namespace GanttIsland
{
    // =============================================================================================
    // GanttPage - the "screen" of this sample: the Gantt chart on the left and an Event Logger on
    // the right. In a normal UWP app this would be MainPage.xaml + MainPage.xaml.cs. This sample
    // runs inside a Win32 C++ window (XAML Islands), which has no XAML compiler, so the same
    // layout is built in C# code instead. Everything you see here maps 1:1 to XAML markup.
    //
    // EXECUTION SEQUENCE (what happens, in order)
    //   1. Bridge.Initialize creates a GanttPage                          -> constructor below runs.
    //   2. Constructor: create data           (SampleData.CreateProjectTasks)
    //   3. Constructor: create SfGantt and bind the data (ItemsSource)     -> Gantt shows the tasks.
    //   4. Constructor: subscribe to the Gantt events (SubscribeToGanttEvents)
    //   5. Constructor: build the page layout (Gantt + Event Logger)
    //   6. Bridge puts RootLayout into the island                          -> the page appears.
    //   7. User interacts (select, edit, drag, resize ...) -> Gantt raises an event
    //                                                      -> our handler runs -> WriteToEventLog.
    // =============================================================================================
    internal sealed class GanttPage
    {
        // Text entries shown in the Event Logger list. ObservableCollection lets the ListView
        // refresh itself whenever an entry is inserted or cleared.
        private readonly ObservableCollection<string> eventLogEntries = new ObservableCollection<string>();

        // The Syncfusion Gantt control.
        public SfGantt Gantt { get; }

        // The root tasks bound to the Gantt (see SampleData). Kept so the host can reach the data.
        public ObservableCollection<TaskDetail> TaskCollection { get; }

        // The top-level layout (Gantt + Event Logger). The host places this inside the Win32 window.
        public Grid RootLayout { get; }

        // Entry point of the page. Runs once when the page is created.
        // It (1) creates the data, (2) creates the Gantt and binds the data to it, (3) subscribes
        // to the events and (4) builds the layout that contains the Gantt and the Event Logger.
        public GanttPage()
        {
            // 1. CREATE THE DATA - see SampleData.cs. Nothing is displayed yet; this is only objects in memory.
            TaskCollection = SampleData.CreateProjectTasks();

            // 2. CREATE THE GANTT AND BIND THE DATA.
            Gantt = CreateGanttControl(TaskCollection);

            // 3. SUBSCRIBE TO EVENTS so we are told when the user interacts with the chart.
            SubscribeToGanttEvents();

            // 4. BUILD THE LAYOUT that places the Gantt and the Event Logger side by side.
            RootLayout = CreatePageLayout();
        }

        // ------------------------------------------------------------------------------------
        // DATA BINDING AND RENDERING
        // ------------------------------------------------------------------------------------

        // Creates the SfGantt control and gives it the data.
        //
        // HOW THE DATA IS BOUND
        //   ItemsSource = taskCollection   <- this single line connects the data to the chart.
        //   The Gantt reads each root TaskDetail, then walks its Children recursively to build the tree.
        //
        // HOW THE GANTT RENDERS THE TASKS
        //   The control has two halves that are filled from the same data:
        //     * the grid on the left   - one row per task (ID, Name, Start, Finish, ...),
        //     * the chart on the right - one task bar per task on a timeline. Bar position and length
        //       come from StartDate/FinishDate, the filled part from Progress, the connector lines
        //       from Predecessors, and the resource names from Resources.
        //   Summary tasks (tasks with Children) are drawn as summary bars and can be expanded/collapsed.
        //   Because the data is an ObservableCollection, later changes to it repaint the chart automatically.
        private SfGantt CreateGanttControl(ObservableCollection<TaskDetail> taskCollection)
        {
            return new SfGantt
            {
                // ---- Data ----
                ItemsSource = taskCollection,                                  // the task hierarchy
                ProjectResources = SampleData.CreateProjectResources(),        // the people that tasks can use

                // ---- Appearance ----
                HighlightCriticalTasks = true,                                 // mark tasks that decide the project end date
                CriticalTaskBarBrush = new SolidColorBrush(Colors.Red),        // ... in red

                // ---- Behaviour ----
                AllowEditing = true,                                           // cells and task bars can be edited
                AllowSorting = true,                                           // column headers can sort the grid

                // ---- Timeline header: top row shows weeks, bottom row shows days ----
                TimescaleSettings = new TimescaleSettings
                {
                    TopTier = new TimescaleTier { IntervalType = IntervalType.Weeks },
                    BottomTier = new TimescaleTier { IntervalType = IntervalType.Days },
                },

                // ---- Holidays ----
                // Holidays are only drawn when non-working days are shown.
                // They are set in code because a binding inside a collection would not resolve.
                ShowNonWorkingDays = true,
                Holidays = new GanttHolidayCollection
                {
                    new GanttHoliday
                    {
                        Day = new DateTime(2026, 11, 26),                      // Thanksgiving
                        Background = new SolidColorBrush(Colors.CadetBlue),
                    },
                },
            };
        }

        // ------------------------------------------------------------------------------------
        // EVENTS
        // ------------------------------------------------------------------------------------

        // Subscribes to the Gantt events.
        //
        // HOW EVENT SUBSCRIPTION WORKS
        //   "Gantt.EventName += HandlerMethod;" tells the control to call HandlerMethod whenever that
        //   event happens. Every handler has the same shape:  void Handler(object sender, SomeEventArgs args)
        //     sender - the control that raised the event (here, always the Gantt).
        //     args   - the data about what happened. Its properties differ per event (see each handler).
        //
        // WHEN THE EVENTS ARE RAISED
        //   Events are raised by the Gantt itself, in response to user actions, never by our code.
        //   "-ing" events (SelectionChanging, TaskBarProgressChanging ...) fire BEFORE the change
        //   and "-ed" events (SelectionChanged, TaskBarProgressChanged ...) fire AFTER it.
        private void SubscribeToGanttEvents()
        {
            // Selecting a row.
            Gantt.SelectionChanging += OnSelectionChanging;
            Gantt.SelectionChanged += OnSelectionChanged;

            // Editing a cell in the grid.
            Gantt.BeginCellEdit += OnBeginCellEdit;
            Gantt.EndCellEdit += OnEndCellEdit;

            // Dragging a task bar to move it to other dates.
            Gantt.TaskBarDragStart += OnTaskBarDragStart;
            Gantt.TaskBarDragDelta += OnTaskBarDragDelta;
            Gantt.TaskBarDragCompleted += OnTaskBarDragCompleted;

            // Dragging the edge of a task bar to change its length.
            Gantt.TaskBarResizeStart += OnTaskBarResizeStart;
            Gantt.TaskBarResizeDelta += OnTaskBarResizeDelta;
            Gantt.TaskBarResizeCompleted += OnTaskBarResizeCompleted;

            // Dragging the progress thumb of a task bar.
            Gantt.TaskBarProgressChanging += OnTaskBarProgressChanging;
            Gantt.TaskBarProgressChanged += OnTaskBarProgressChanged;

            // Drawing a dependency line from one task bar to another.
            Gantt.TaskRelationshipEstablished += OnTaskRelationshipEstablished;

            // Expanding / collapsing a summary task.
            Gantt.TaskExpanding += OnTaskExpanding;
            Gantt.TaskExpanded += OnTaskExpanded;
            Gantt.TaskCollapsing += OnTaskCollapsing;
            Gantt.TaskCollapsed += OnTaskCollapsed;
        }

        // HOW TO READ EVENT DATA IN A HANDLER
        //   The affected task is on the event args as "Task" or "Item" (typed as object). Cast it to
        //   TaskDetail to read its properties:   var task = eventArgs.Task as TaskDetail;
        //   Events that describe a change also carry the old and new values (for example
        //   OldStartDate / NewStartDate, OldValue / NewValue).
        //   The handlers below only pass these values to WriteToEventLog; put your own logic
        //   (validation, saving to a database, calling native code ...) in the same place.

        // ---- Selection ---------------------------------------------------------------------
        // Raised just before the selected row changes. args.Item = the row being selected, args.Index = its row index.
        private void OnSelectionChanging(object sender, GanttSelectionChangingEventArgs eventArgs)
        {
            WriteToEventLog("SelectionChanging (index " + eventArgs.Index + ")", eventArgs.Item);
        }

        // Raised after the selected row changed.
        private void OnSelectionChanged(object sender, GanttSelectionChangedEventArgs eventArgs)
        {
            WriteToEventLog("SelectionChanged (index " + eventArgs.Index + ")", eventArgs.Item);
        }

        // ---- Cell editing ------------------------------------------------------------------
        // Raised when the user starts editing a cell. args.MappingName = the edited column (e.g. "Name").
        private void OnBeginCellEdit(object sender, BeginCellEditEventArgs eventArgs)
        {
            WriteToEventLog("BeginCellEdit [" + eventArgs.MappingName + "]", eventArgs.Item);
        }

        // Raised when the edit is committed. args.OldItem = the task before the edit, args.Item = after it.
        private void OnEndCellEdit(object sender, EndCellEditEventArgs eventArgs)
        {
            WriteToEventLog("EndCellEdit [" + eventArgs.MappingName + "]", eventArgs.Item,
                            FormatDateRange(eventArgs.OldItem as TaskDetail), FormatDateRange(eventArgs.Item as TaskDetail));
        }

        // ---- Dragging a task bar (changes the dates, keeps the duration) -------------------
        // Raised when the user presses on a task bar and begins to drag it.
        private void OnTaskBarDragStart(object sender, TaskBarDragStartEventArgs eventArgs)
        {
            WriteToEventLog("TaskBarDragStart", eventArgs.Task, FormatDateRange(eventArgs.Task as TaskDetail));
        }

        // Raised continuously while dragging. args.NewStartDate / NewFinishDate = where the bar would land now.
        private void OnTaskBarDragDelta(object sender, TaskBarDragDeltaEventArgs eventArgs)
        {
            WriteToEventLog("TaskBarDragDelta", eventArgs.Task, null,
                            FormatDate(eventArgs.NewStartDate) + " to " + FormatDate(eventArgs.NewFinishDate));
        }

        // Raised when the user releases the bar. args.OldStartDate / OldFinishDate = the dates before the drag.
        private void OnTaskBarDragCompleted(object sender, TaskBarDragCompletedEventArgs eventArgs)
        {
            WriteToEventLog("TaskBarDragCompleted", eventArgs.Task,
                            FormatDate(eventArgs.OldStartDate) + " to " + FormatDate(eventArgs.OldFinishDate),
                            FormatDateRange(eventArgs.Task as TaskDetail));
        }

        // ---- Resizing a task bar (changes the duration) ------------------------------------
        // args.ResizingDirection tells which edge is dragged (start or finish).
        private void OnTaskBarResizeStart(object sender, TaskBarResizeStartEventArgs eventArgs)
        {
            WriteToEventLog("TaskBarResizeStart (" + eventArgs.ResizingDirection + ")", eventArgs.Task,
                            FormatDateRange(eventArgs.Task as TaskDetail));
        }

        // Raised continuously while resizing. args.ModifiedValue = the date the dragged edge is on now.
        private void OnTaskBarResizeDelta(object sender, TaskBarResizeDeltaEventArgs eventArgs)
        {
            WriteToEventLog("TaskBarResizeDelta (" + eventArgs.ResizingDirection + ")", eventArgs.Task, null,
                            eventArgs.ModifiedValue.ToString());
        }

        // Raised when the user releases the edge. args.OldValue = the date before resizing.
        private void OnTaskBarResizeCompleted(object sender, TaskBarResizeCompletedEventArgs eventArgs)
        {
            WriteToEventLog("TaskBarResizeCompleted (" + eventArgs.ResizingDirection + ")", eventArgs.Task,
                            eventArgs.OldValue.ToString(), FormatDateRange(eventArgs.Task as TaskDetail));
        }

        // ---- Progress ----------------------------------------------------------------------
        // Raised while the progress thumb is dragged. args.NewValue = the percentage it is moving to.
        private void OnTaskBarProgressChanging(object sender, TaskBarProgressChangingEventArgs eventArgs)
        {
            WriteToEventLog("TaskBarProgressChanging", eventArgs.Task, null, eventArgs.NewValue.ToString());
        }

        // Raised after the progress changed. args.OldValue / NewValue = the percentage before and after.
        private void OnTaskBarProgressChanged(object sender, TaskBarProgressChangedEventArgs eventArgs)
        {
            WriteToEventLog("TaskBarProgressChanged", eventArgs.Task, eventArgs.OldValue.ToString(), eventArgs.NewValue.ToString());
        }

        // ---- Dependencies ------------------------------------------------------------------
        // Raised when the user connects two task bars. Only creation is exposed as an event.
        // args.StartTask = the predecessor, args.EndTask = the successor, args.Relationship = the type (e.g. FinishToStart).
        private void OnTaskRelationshipEstablished(object sender, TaskRelationshipEventArgs eventArgs)
        {
            TaskDetail predecessorTask = eventArgs.StartTask as TaskDetail;
            TaskDetail successorTask = eventArgs.EndTask as TaskDetail;

            WriteToEventLog("TaskRelationshipEstablished", eventArgs.StartTask, null,
                            predecessorTask?.ID + " -> " + successorTask?.ID + " (" + eventArgs.Relationship + ")");
        }

        // ---- Expand / collapse (args.Item is the summary task being expanded or collapsed) --
        private void OnTaskExpanding(object sender, TaskExpandingEventArgs eventArgs)
        {
            WriteToEventLog("TaskExpanding", eventArgs.Item);
        }

        private void OnTaskExpanded(object sender, TaskExpandedEventArgs eventArgs)
        {
            WriteToEventLog("TaskExpanded", eventArgs.Item);
        }

        private void OnTaskCollapsing(object sender, TaskCollapsingEventArgs eventArgs)
        {
            WriteToEventLog("TaskCollapsing", eventArgs.Item);
        }

        private void OnTaskCollapsed(object sender, TaskCollapsedEventArgs eventArgs)
        {
            WriteToEventLog("TaskCollapsed", eventArgs.Item);
        }

        // ------------------------------------------------------------------------------------
        // EVENT LOGGER (shows what each event delivered)
        // ------------------------------------------------------------------------------------

        // Adds one entry at the top of the Event Logger: time, event name, task id and name,
        // and (when the event supplies them) the old and new values.
        // "affectedTask" is whatever object the event args gave us; we cast it to TaskDetail to read its properties.
        private void WriteToEventLog(string eventName, object affectedTask, string oldValue = null, string newValue = null)
        {
            TaskDetail task = affectedTask as TaskDetail;

            StringBuilder entryText = new StringBuilder();
            entryText.AppendLine("Time: " + DateTime.Now.ToString("hh:mm:ss tt"));
            entryText.AppendLine("Event: " + eventName);
            entryText.AppendLine("Task ID: " + (task?.ID ?? "-"));
            entryText.AppendLine("Task Name: " + (task?.Name ?? "-"));
            if (oldValue != null) entryText.AppendLine("Old Value: " + oldValue);
            if (newValue != null) entryText.AppendLine("New Value: " + newValue);

            eventLogEntries.Insert(0, entryText.ToString());

            // GANTT -> C++ : hand the same information to the native application (if it registered a callback).
            // This runs on the UI thread, because the Gantt raises its events there.
            if (NativeEventForwarder != null)
            {
                string details = (oldValue != null || newValue != null)
                    ? "Old: " + (oldValue ?? "-") + " | New: " + (newValue ?? "-")
                    : string.Empty;
                NativeEventForwarder(eventName, task?.ID ?? string.Empty, details);
            }
        }

        // Set by Bridge.SetEventCallback. Arguments: event name, task id, old/new value text.
        // Every Gantt event passes through WriteToEventLog, so one call there covers all events.
        public Action<string, string, string> NativeEventForwarder { get; set; }

        // C++ -> GANTT : adds tasks received from the native application as new top-level tasks (added to the collection that ItemsSource is bound to).
        // Each line is "id|name|yyyy-MM-dd|yyyy-MM-dd|progress". Because TaskCollection is an
        // ObservableCollection tree, the new rows and task bars appear without any refresh call.
        public int AddTasksFromText(string taskLines)
        {
            int addedCount = 0;

            foreach (string line in taskLines.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.Trim().Split('|');
                if (fields.Length != 5) continue;

                TaskCollection.Add(new TaskDetail
                {
                    ID = fields[0],
                    Name = fields[1],
                    StartDate = DateTime.ParseExact(fields[2], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    FinishDate = DateTime.ParseExact(fields[3], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    Progress = double.Parse(fields[4], System.Globalization.CultureInfo.InvariantCulture),
                });
                addedCount++;
            }
            return addedCount;
        }

        // Formats a date as e.g. "26-Nov-2026".
        private static string FormatDate(DateTime date)
        {
            return date.ToString("dd-MMM-yyyy");
        }

        // Formats a task's dates as "02-Nov-2026 to 04-Nov-2026" (null when there is no task).
        private static string FormatDateRange(TaskDetail task)
        {
            return task == null ? null : FormatDate(task.StartDate) + " to " + FormatDate(task.FinishDate);
        }

        // ------------------------------------------------------------------------------------
        // PAGE LAYOUT
        // ------------------------------------------------------------------------------------

        // Builds the page: a two-column grid with the Gantt on the left (3 parts of the width)
        // and the Event Logger on the right (1 part, at least 320 px wide).
        private Grid CreatePageLayout()
        {
            Grid pageLayout = new Grid();
            pageLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            pageLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });

            Grid.SetColumn(Gantt, 0);
            pageLayout.Children.Add(Gantt);

            Grid eventLoggerPanel = CreateEventLoggerPanel();
            Grid.SetColumn(eventLoggerPanel, 1);
            pageLayout.Children.Add(eventLoggerPanel);

            return pageLayout;
        }

        // Builds the Event Logger panel: a title with "Clear Log" and "Copy Log" buttons above a list of log entries.
        private Grid CreateEventLoggerPanel()
        {
            Grid panel = new Grid { Margin = new Thickness(8) };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            StackPanel headerRow = new StackPanel { Orientation = Orientation.Horizontal };
            headerRow.Children.Add(new TextBlock
            {
                Text = "Event Logger",
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });

            Button clearLogButton = new Button { Content = "Clear Log", Margin = new Thickness(0, 0, 8, 0) };
            clearLogButton.Click += OnClearLogClicked;
            headerRow.Children.Add(clearLogButton);

            Button copyLogButton = new Button { Content = "Copy Log" };
            copyLogButton.Click += OnCopyLogClicked;
            headerRow.Children.Add(copyLogButton);

            panel.Children.Add(headerRow);

            // The list shows eventLogEntries; new entries appear automatically (ObservableCollection).
            ListView logListView = new ListView { ItemsSource = eventLogEntries, SelectionMode = ListViewSelectionMode.None };
            Grid.SetRow(logListView, 1);
            panel.Children.Add(logListView);

            return panel;
        }

        // "Clear Log" button: removes every entry from the Event Logger.
        private void OnClearLogClicked(object sender, RoutedEventArgs eventArgs)
        {
            eventLogEntries.Clear();
        }

        // "Copy Log" button: copies all entries to the Windows clipboard as plain text.
        private void OnCopyLogClicked(object sender, RoutedEventArgs eventArgs)
        {
            DataPackage clipboardContent = new DataPackage();
            clipboardContent.SetText(string.Join(Environment.NewLine, eventLogEntries));
            Clipboard.SetContent(clipboardContent);
        }
    }
}
