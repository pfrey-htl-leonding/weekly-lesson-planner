# Weekly Lesson Planner — Implementation Plan

## 1. Confirmed requirements

The application replaces the error-prone manual shifting of lesson topics in the reference spreadsheet with a graphical planner that understands course days and fixed, non-teaching days.

The following requirements and interpretations are confirmed:

1. **Calendar orientation:** one row per week, weekday columns from Monday through Friday by default, and the week number at the left. Saturday and Sunday may be shown when configured as course days.
2. **Week numbering:** use ISO 8601 week numbers.
3. **Frontend:** Angular with Angular Material and Angular CDK drag-and-drop.
4. **Backend:** .NET 10 Minimal Web API.
5. **Database:** PostgreSQL accessed through an Entity Framework `DbContext`.
6. **Planning logic:** implement the scheduling rules in a scoped planning service registered with and instantiated by ASP.NET Core dependency injection. Minimal API endpoints remain thin and delegate all schedule mutations to this service.
7. **Day-marker scope and exclusivity:** holiday and event markers belong to the underlying calendar date and apply to every course. Exams belong to one course. For any course/date view, the effective state is exactly one of normal, holiday, event, or exam. A global holiday/event and a course exam cannot coexist on the same date; holiday and event are also mutually exclusive with each other.
8. **Topic list:** show unplanned topic instances, sorted alphabetically. Placing an instance removes it from the list; removing or overwriting it puts that instance back into the list.
9. **Repeated topics:** a scheduled topic has a **Copy** action. Copying creates one new, unplanned instance of the same topic and puts it into the topic list. The scheduled source instance remains unchanged.
10. **Descriptions:** the topic description is sufficient; no separate URL or branch-name field is required.
11. **Spreadsheet migration:** the ODS workbook is a reference only. Automatic import is outside the MVP.
12. **Export:** provide CSV schedule export.
13. **Deployment:** run the Angular frontend, .NET API, and PostgreSQL database as a Docker Compose stack.
14. **Users:** no authentication, authorization, or user management is required.

## 2. Confirmed interaction semantics

1. **Insert with shifting:** cascading stops as soon as the displaced topic instances fit into available eligible empty days. For a single inserted instance, the first later eligible empty day absorbs the cascade; assignments after that gap remain unchanged.
2. **Delete with shifting:** later assignments move backward only far enough to close the newly created gap. Pre-existing later gaps are preserved once all topics affected by the deletion have been placed.
3. **Drag-and-drop:** for both list-to-calendar placement and calendar-to-calendar movement, only frontend drag state and the standard drop indicator change while dragging. There is no live schedule preview and no planning API call during pointer movement. On a valid drop, the frontend sends one command. A scheduled-topic move atomically removes and inserts the topic: the removal phase respects “Delete shifts schedule,” and the placement phase respects “Insert shifts schedule.” It is not a separate reorder operation.
4. **Creating a fixed day:** adding a global holiday/event automatically shifts affected assignments forward independently for every course that teaches on that date. Adding a course exam shifts only that course. Each affected sequence stops when eligible empty days absorb its displaced topics. If any affected course lacks capacity, reject and roll back the complete marker change. Adding a global marker on a date that already has any course exam, or adding an exam on a global holiday/event, is rejected because effective day states are exclusive.
5. **CSV scope:** export only the currently selected course over the configured date range. Emit one row per relevant calendar day with course, date, ISO week, weekday, day state, marker label, topic heading, and topic description.
6. **Checkbox defaults:** both shift checkboxes initially default to selected. They are UI preferences rather than shared planning data unless cross-browser persistence is later requested.

## 3. Proposed architecture

### Frontend

Build a standalone Angular application using strict TypeScript, Angular Material, and Angular CDK drag-and-drop. The frontend owns presentation state, forms, drag interactions, and the two shift-checkbox values. It does not implement authoritative planning behavior or access PostgreSQL directly.

Use typed API clients generated from, or kept consistent with, the Web API's OpenAPI document. During local development, proxy `/api` to the backend. In the production stack, Nginx serves the Angular files and reverse-proxies `/api` to the API container so the browser uses one origin.

### Backend

Build an ASP.NET Core .NET 10 Minimal Web API. Register these primary scoped services:

- `PlannerDbContext`: Entity Framework context for PostgreSQL.
- `IPlanningService` / `PlanningService`: all placement, overwrite, remove, shift, copy, drag, global-marker, and course-exam conflict operations.
- Query/export services for schedule reads and CSV streaming where separating them keeps the planning service focused.

Endpoints validate transport-level input, call the appropriate service, and translate domain results into consistent HTTP responses. The planning service validates business rules and performs every multi-row change in a database transaction.

Use OpenAPI, Problem Details, structured logging, readiness/liveness endpoints, configuration from environment variables, and development-only CORS where the Angular proxy is not used. Never store the PostgreSQL password in source control.

### Entity Framework provider

Target `net10.0` and EF Core 10 with the stable `Npgsql.EntityFrameworkCore.PostgreSQL` 10.x provider. Pin compatible patch versions centrally and commit the dependency lock files. Run a small compatibility test against the selected PostgreSQL image before building the full data layer, covering migrations, transactions, uniqueness violations, `xmin`-based optimistic concurrency, and `DateOnly` mapping.

### Deployment

Use Docker Compose with three services:

- `frontend`: Nginx serving the Angular build and proxying `/api`.
- `api`: the .NET 10 Minimal Web API.
- `db`: PostgreSQL with a named volume, `pg_isready` health check, non-superuser application role, and credentials supplied through environment/secrets configuration.

Build frontend and API images with multi-stage Dockerfiles. Run Entity Framework migrations through an explicit migration/startup step after PostgreSQL is healthy and before the API accepts traffic.

## 4. Domain and database model

The initial EF Core model should contain:

- **AppConfig**: inclusive planning-range start/end dates, visible weekdays, display colours, and ISO week convention.
- **Course**: name and description.
- **CourseWeekday**: course and weekday defining recurring lesson slots. Include a slot ordinal only if multiple lessons for one course can occur on the same date.
- **GlobalDayMarker**: calendar date, type (`Holiday` or `Event`), and optional label. A unique constraint on date makes holiday and event mutually exclusive. The marker blocks that date for every course.
- **CourseExam**: course, calendar date, and exam name. A unique constraint on course/date allows at most one exam for that course on the date. Planning-service validation prevents exams on dates having a global marker and prevents a new global marker on a date having any exams.
- **Topic**: the reusable definition containing course, heading, and description.
- **TopicInstance**: one independently schedulable instance of a topic. Creating a topic creates its first unplanned instance; **Copy** creates one more instance pointing to the same topic definition.
- **TopicAssignment**: course, topic instance, and calendar date. Unique constraints on course/date and topic instance guarantee that at most one topic instance occupies a course day and that one instance occupies at most one day. Composite relationships through the topic definition guarantee at database level that the instance belongs to the assigned course.

An exam is a `CourseExam`, not a topic. It blocks only its course/date and cannot coexist with a `TopicAssignment` for that course/date. A holiday or event blocks all course assignments on its date. Marker and assignment commands must use transaction isolation/locking sufficient to preserve these cross-table exclusivity rules under concurrent requests.

A topic instance is **unplanned** when it has no `TopicAssignment` row and **planned** when it has one. The unplanned topic list is therefore a query result, not mutable status stored separately. The **Copy** command creates a new instance linked to the same topic definition and with no assignment. Instances move independently, while editing the shared topic heading or description updates every instance.

Use generated stable IDs, foreign keys, indexes, maximum string lengths, PostgreSQL's `xmin` concurrency token where schedule writes can conflict, and migrations from the first schema version. Store calendar dates as .NET `DateOnly` values mapped to PostgreSQL `date` columns to avoid time-zone and daylight-saving errors.

## 5. Scheduling rules

`PlanningService` is the only component allowed to mutate `TopicAssignment` rows. It uses `PlannerDbContext` through constructor injection and implements these rules:

1. Generate a course's eligible lesson-slot sequence from its configured weekdays and inclusive planning range.
2. Skip dates having a global holiday/event and dates having an exam for the course being planned.
3. Reject assignments on the wrong weekday, on a fixed day, outside the planning range, for another course, or in an impossible target state.
4. Apply each command and all resulting shifts in one database transaction.

### Place an unplanned topic

- If the target slot is empty, assign the topic there regardless of the “Insert shifts schedule” value.
- If the target is occupied and **Insert shifts schedule is selected**, move the existing assignment to the next eligible slot and cascade occupied assignments forward only until eligible empty days provide enough capacity for the displaced instances. Place the new topic at the target. For one inserted instance, shifting stops at the first later eligible empty day.
- If no later slot is available, reject and roll back the complete operation; the new topic remains unplanned.
- If the target is occupied and **Insert shifts schedule is cleared**, remove the existing assignment without confirmation and place the new topic at the target. The overwritten topic instance returns to the unplanned list.

### Remove a scheduled topic instance

- Always remove the selected assignment. The removed topic instance returns to the unplanned list.
- If **Delete shifts schedule is selected**, shift later assignments backward only far enough to close the new gap, skipping fixed dates and preserving pre-existing later gaps once the affected sequence is compacted.
- If **Delete shifts schedule is cleared**, leave the target day empty.

### Copy a topic

- The **Copy** action on a scheduled topic creates one new instance linked to the same topic definition.
- The new instance is unplanned and appears alphabetically in the topic list; the source topic remains scheduled and unchanged.
- Moving or removing either instance does not affect the other. Editing their shared heading or description affects all instances of that topic.

### Drag a scheduled topic

- Keep all activity before drop inside the Angular UI. Do not mutate application data, call the planning API, or calculate/display a live shifted-schedule preview while the pointer is moving.
- On a valid drop, send exactly one drag command containing the source, destination, and current values of both shift checkboxes. An invalid or cancelled drop sends no command and leaves the schedule unchanged.
- Treat the drag as a remove command at the source followed by an insert command at the destination, within one database transaction.
- The source removal applies the current `deleteShiftsSchedule` value. The destination placement then applies the current `insertShiftsSchedule` value against the resulting schedule.
- Do not expose the intermediate unplanned state to other requests or the UI. If either phase fails, roll back both phases and leave the schedule unchanged.
- Preserve every topic instance exactly once; a drag must never duplicate or lose it.

### Configuration and marker changes

- Before shortening the planning range or removing a course weekday, calculate affected assignments and require an explicit resolution before applying a destructive change.
- When adding a global holiday/event to an occupied date, calculate and cascade the affected schedule separately for every course. Skip each course's other fixed dates and stop when enough eligible free days have been used.
- When adding an exam to an occupied date, cascade only the selected course's affected schedule using the same rules.
- Reject a global marker if any course has an exam on that date, and reject an exam if the date has a global marker.
- If any affected course lacks sufficient later capacity, reject and roll back the entire marker change, including shifts already calculated for other courses.
- Changing marker colour or text does not recalculate the schedule.

The service should return an impact result containing the inserted/removed assignment, displaced topics, affected dates, and any topic that became unplanned. The frontend uses this result to refresh state and communicate what changed.

## 6. API outline

Define resource endpoints for:

- Configuration and planning range.
- Courses and course weekdays.
- Global holiday/event markers.
- Course-specific exams.
- Topics, including an unplanned-topics query sorted alphabetically.
- Course schedules over a date range.

Define command endpoints rather than exposing raw assignment CRUD for:

- Place a topic with `insertShiftsSchedule`.
- Copy a scheduled topic into a new unplanned topic instance.
- Remove a scheduled topic instance with `deleteShiftsSchedule`.
- Drag a scheduled topic using both `deleteShiftsSchedule` and `insertShiftsSchedule`.
- Add global markers and course exams through planning commands that resolve affected assignments atomically.
- Resolve assignments affected by planning-range or course-weekday changes.
- Export a schedule as `text/csv`.

Return validation conflicts such as occupied/fixed dates, insufficient remaining slots, and concurrent updates as structured Problem Details responses. Keep command contracts explicit so the frontend cannot accidentally bypass planning logic.

## 7. User interface plan

Create an Angular Material application shell containing:

- A course selector and access to course/configuration management.
- An **All topics** course-view option that renders placed topics from every course and identifies each topic's course.
- A schedule board with sticky ISO week/date labels, week rows, weekday columns, and topic cards in eligible cells.
- Global holiday/event colours, icons, labels, and editing controls on the shared time axis, plus course-specific exam controls in the selected course view.
- An alphabetically sorted topic-management panel containing only unplanned topics, with create, edit, delete, search, and drag handles.
- Topic dialogs for heading and description.
- An “Insert shifts schedule” checkbox adjacent to placement controls.
- A “Delete shifts schedule” checkbox adjacent to removal controls.
- Drag-and-drop from the unplanned topic list to the calendar and between scheduled calendar cells.
- During dragging, show only normal drag/drop affordances such as the dragged card and valid target highlight; do not show a live preview of shifted topics.
- Keyboard-accessible place, move earlier/later, copy, and remove controls using the same API commands as drag-and-drop.
- The forward-arrow action is a dedicated one-slot forward shift: it leaves the source lesson day empty and cascades the destination sequence forward, independent of the two general drag/drop checkboxes.
- A **Copy** action on each scheduled topic card that puts one copied, unplanned instance into the topic list.
- CSV export for the selected course/date range.
- Clear capacity/conflict errors and progress states. Overwrite mode intentionally does not show a confirmation dialog.
- Management tabs ordered as Topic management, Course exam, Global holiday, Courses, Planning range, and Options; topic search/list precedes topic add/edit controls.
- Responsive horizontal calendar scrolling rather than unreadably narrow cells.

Use text/icons in addition to colour so the day states remain distinguishable for users with colour-vision deficiencies.

## 8. Implementation phases

### Phase 0 — Confirm behavior and prove the stack

- Record the confirmed interaction semantics from Section 2 as executable service test cases.
- Scaffold the .NET 10 solution and Angular workspace.
- Prove a Minimal API endpoint can resolve a scoped `PlanningService` and `PlannerDbContext` through dependency injection.
- Test Npgsql with a PostgreSQL connection, migration, transaction, uniqueness violation, concurrency token, `DateOnly`, and production container behavior.
- Define wireframes for the schedule board, checkboxes, Copy action, and fixed-day shift/capacity flow.

**Exit criterion:** the confirmed interaction rules are recorded and the .NET 10/EF Core 10/Npgsql/PostgreSQL combination works in Docker.

### Phase 1 — Project and deployment foundation

- Establish API, application/domain, and automated-test project boundaries without adding unnecessary layers.
- Configure Minimal API routing, OpenAPI, Problem Details, logging, health checks, and environment validation.
- Configure Angular routing, Material, typed API access, error handling, and development proxy.
- Add frontend/API Dockerfiles, Nginx proxy configuration, PostgreSQL service/volume, and Docker Compose health/dependency rules.
- Add a repeatable EF migration workflow.

**Exit criterion:** the production stack starts, serves Angular, reaches a healthy API, and initializes an empty versioned PostgreSQL database.

### Phase 2 — Data model, configuration, and calendar

**Implementation status:** completed and verified on 2026-08-13.

- Implement EF entities, mappings, constraints, indexes, and initial migration.
- Implement configuration and inclusive planning-range APIs.
- Generate deterministic ISO 8601 week/calendar views.
- Implement course CRUD and recurring weekday selection.
- Implement global holiday/event CRUD and course-specific exam CRUD through planning-service commands, including exclusivity and capacity validation.
- Build the Angular configuration, course, and calendar views.

**Exit criterion:** the school-year grid renders global holiday/events for every course, course exams only for their course, and persists exclusive effective day states in PostgreSQL.

### Phase 3 — Topic management

**Implementation status:** completed and verified on 2026-08-13.

- Implement topic-definition CRUD, automatic creation of the first instance, and the alphabetically sorted unplanned-instance query.
- Define deletion semantics explicitly: deleting an unplanned list entry removes that instance; deleting the shared topic definition is allowed only when none of its instances are scheduled and removes all its unplanned instances.
- Implement copying a scheduled topic into one new unplanned instance of the same definition.
- Build topic management, validation, searching, and editing UI.
- Verify that topic instances enter and leave the unplanned list based solely on assignment presence.

**Exit criterion:** reusable topics can be managed and their planned/unplanned visibility is correct after reload.

### Phase 4 — Planning service

**Implementation status:** completed and verified on 2026-08-17.

- Implement eligible-slot generation and course/day validation.
- Implement transactional placement into an empty day.
- Implement checked insertion with cascading forward shifts.
- Implement unchecked insertion with immediate overwrite and unplanned-topic recalculation.
- Implement checked/unchecked deletion behavior.
- Implement atomic drag as checkbox-aware removal followed by checkbox-aware insertion.
- Integrate the Phase 3 Copy command with scheduling mutations and impact results.
- Implement impact results, all-course shifting for global markers, single-course shifting for exams, and resolution of planning-range/course-weekday changes.
- Test the service against a real containerized PostgreSQL instance, not an in-memory substitute for database-specific behavior.

Verification: the scheduling API exposes transactional `place`, `remove`, and `drag` commands with impact results. The shared mutation engine is covered for all four drag checkbox combinations, first-gap insertion/deletion boundaries, multi-day fixed markers, and capacity failure. PostgreSQL integration tests cover persistence, all-course global-marker shifts, selected-course exam shifts, and rollback with a completely full schedule. Destructive planning-range and course-weekday changes remain explicitly rejected until their affected topics are moved or removed through these planning commands.

**Exit criterion:** all planning mutations and rollback cases work through `IPlanningService` independently of the UI.

### Phase 5 — Interactive planner

**Implementation status:** completed and verified on 2026-08-17.

- Bind the schedule board and unplanned topic list to API queries.
- Add both shift checkboxes and pass their values in command requests.
- Add list-to-calendar placement, checkbox-aware scheduled-topic dragging, Copy controls, and visual drop restrictions.
- Add accessible button/keyboard alternatives that call the same backend commands.
- Refresh affected schedule and topic-list state from command impact results.
- Check usability with a realistic full school year and the patterns in the reference workbook.

Verification: the selected-course view connects the alphabetic unplanned list and eligible calendar cells with drop-only CDK drag interactions. It exposes both shift checkboxes, place/move/copy/remove button alternatives, read-only all-course aggregation, fixed-day drop restrictions, compact topic cards, and impact-aware success messages followed by authoritative calendar/list refreshes. Angular tests verify that merely entering a drop target sends no request, valid drops send exactly one checkbox-aware command, and invalid, cancelled, or same-day interactions do not mutate the schedule. The production Angular bundle and complete backend regression suite pass, and the Compose frontend is deployed on the full school-year calendar.

**Exit criterion:** planning, overwrite, insertion shift, deletion with/without shift, copying, and drag workflows operate without manual date repair.

### Phase 6 — CSV export and production hardening

- Implement escaped, UTF-8 CSV generation from backend query results and download from Angular.
- Add PostgreSQL backup/restore documentation using `pg_dump`/`pg_restore` in addition to volume recovery guidance.
- Complete accessibility, responsive-layout, concurrency, security-header, and performance passes.
- Document local development, migrations, tests, configuration, Docker startup, export, and operational recovery.
- Build the final images and exercise the critical workflow end to end.

**Exit criterion:** the documented MVP is containerized, exportable, recoverable, and passes its automated acceptance suite.

## 9. Verification strategy

### Planning-service unit tests

- Inclusive date ranges, leap years, year boundaries, and ISO 8601 week numbers.
- Eligible slots for courses with one or multiple weekdays.
- Global holiday/event scope, course-specific exam scope, and exclusive effective day states.
- Initial placement into an empty slot.
- Checked insertion at beginning/middle/end, including consecutive fixed days; verify that a later empty slot absorbs the cascade and assignments beyond it do not move.
- Checked insertion with no remaining capacity and complete rollback.
- Unchecked overwrite and return of the displaced topic instance to the unplanned list.
- Checked deletion that closes only the new gap, plus unchecked deletion that leaves the day empty.
- Copying a scheduled topic, independently moving the new instance, and verifying that shared content edits appear on both instances.
- Atomic drag earlier/later and onto an occupied date for all four insert/delete checkbox combinations.
- For list-to-calendar and calendar-to-calendar dragging: no API request before drop, exactly one command on a valid drop, and no command for a cancelled or invalid drop.
- Drag rollback when either the remove or insert phase cannot complete.
- Changes to planning range and course weekdays.
- Global holiday/event creation across multiple occupied course schedules, including independent forward cascades and whole-command rollback if one course lacks capacity.
- Course-exam creation with single-course shifting.
- Rejection of global markers conflicting with any existing course exam and exams conflicting with a global marker.

### API and PostgreSQL integration tests

- Dependency-injected planning service and scoped `DbContext` behavior.
- Migrations from an empty and previous-version database.
- Foreign-key and unique course/date constraint enforcement.
- Transaction rollback during a failed multi-assignment shift.
- Date-only mapping and ISO-boundary queries.
- Optimistic-concurrency conflicts and Problem Details responses.
- CSV escaping for commas, quotes, line breaks, Unicode, and empty fields.

### Angular and end-to-end tests

- Manage configuration, course weekdays, topics, global holiday/events, and course-specific exams.
- Verify planned instances disappear from and removed/overwritten instances return to the alphabetical list.
- Place onto an occupied date with insertion shifting on and off.
- Remove with deletion shifting on and off.
- Copy a scheduled topic, verify the copy appears in the list, and schedule it on another date.
- Perform checkbox-aware schedule moves using drag-and-drop and keyboard/button controls.
- Add a global marker on an occupied date and verify every affected course shifts; add an exam and verify only its course shifts.
- Export and parse the selected course's CSV.
- Reload and verify server persistence.
- Run the complete workflow against the production Docker Compose stack.

## 10. MVP acceptance criteria

The MVP is complete when a user can:

- Configure an inclusive school-year range and see it as ISO week rows with weekday columns.
- Create a course occurring on one or more weekdays.
- Create and edit topics with headings and descriptions.
- See only unplanned topics in an alphabetically sorted management list.
- Mark a calendar date globally as holiday or event and see it block every course; mark a date as a named exam for one course and see it block only that course. Effective states remain exclusive and distinguishable without relying only on colour.
- Place a topic only into an eligible normal course day.
- Use “Insert shifts schedule” to choose between cascading later assignments and overwriting without confirmation.
- Use “Delete shifts schedule” to choose between moving later assignments backward and leaving an empty day.
- See an overwritten or removed topic instance return to the unplanned list.
- Use **Copy** on a scheduled topic to put another independently schedulable instance into the unplanned list and later schedule it on another eligible day.
- Move scheduled topics with drag-and-drop or accessible controls; the operation respects both shift checkboxes.
- Add a holiday/event to an occupied date and have every affected course shift forward atomically; add an exam and have only its course shift. A capacity or exclusivity error leaves every schedule and marker unchanged.
- Export the selected course schedule as CSV.
- Reload without losing server-stored data.
- Start the Angular, .NET 10 API, and PostgreSQL production stack with Docker Compose.

## 11. Deferred scope

Unless added as separate requirements, defer user accounts, automatic ODS import, printable reports, lesson attachments, notifications, external school-system integrations, and real-time collaborative editing. Database-level concurrency protection remains in scope even without user accounts because multiple browser tabs or clients can still submit overlapping changes.
