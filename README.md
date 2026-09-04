# A Tool to plan lessons across the school year

## Overview

This is a vibe coding experiment. Using the prompt below "Current situation", I let AI code the tool, mostly using OpenAI Codex. This is vibe coded AND vibe tested AND NOT human reviewed!! So if in doubt, read the code before you start the stack.

It is running as a local-only tool. There is no concept of a user or login. If you want to deploy this across a site, you'd have add your specific user management and extend the data model accordingly.

## Getting and running it

You need [Docker](https://docs.docker.com/get-docker/) with Docker Compose. Clone the repository (recommended, because it makes updates easy):

```bash
git clone https://github.com/pfrey-htl-leonding/weekly-lesson-planner.git
cd weekly-lesson-planner
```

Alternatively, download and extract the ZIP of the `main` branch, then open a terminal in the extracted directory.

Create the local configuration file:

```bash
cp stack/.env.example stack/.env
```

Open `stack/.env` and replace `replace-with-a-local-secret` with a private password. The file is ignored by Git and must not be committed. Then build and start the application:

```bash
docker compose --env-file stack/.env -f stack/compose.yaml up --build -d
```

Open <http://localhost:8080> in a browser. To stop the application without deleting its database, run:

```bash
docker compose --env-file stack/.env -f stack/compose.yaml down
```

After cloning, update to the latest stable version and rebuild with:

```bash
git pull --ff-only
docker compose --env-file stack/.env -f stack/compose.yaml up --build -d
```

See [stack/README.md](./stack/README.md) for health checks, ports, database backups, and data removal.

## Getting started

1. In **School years**, create the planning window by entering its name, start date, and end date.
2. In **Courses**, add a course for that school year and select its teaching days.
3. In **Global holiday**, add holidays and events for the school year. Select one course in **Course view** and use **Course exam** to add its exams.
4. Select the course in **Course view**, then add its topics in **Topic management**. Topic headings that start with a number followed by a space are sorted numerically before other topics—for example, `2 Arrays` comes before `10 Trees`.
5. In **Data import/export**, import or export courses or topics as semicolon-separated CSV (`name;optional description`). Topic import/export requires exactly one selected course.

**Insert shifts schedule** pushes an occupied topic and the following topics forward when you insert another topic; without it, the occupied topic becomes unplanned. **Delete shifts schedule** pulls following topics backward to close a gap after removal; without it, the gap remains.

## Organisation 

Main should be stable. Some milestones are tagged with versions.

# VIBE SECTION

From here, it's vibed...this is the prompt the project was started with. It is not up to date with the current version!

## Current situation

I am planning my lessons across the school year. For every week, the days the lesson occur (sometimes on 2 days, one theory and one lab) are marked and filled with the desired topics. Optionally, a link or a branch name can be added.

The plan is currently done in an Excel with the time axis vertical, the weekdays horizontal. Free days are marked with green colour (e.g. autumn break, Christmas). Test days are marked with yellow. 

When the plan changes, I have to move the topics downward, but need to jump over holidays and test days, which are considered fixed. 

This is tedious and error prone.

## Purpose of the tool

The tool shall help to manage the topics taught in the lessons across the school year. Up to now, this was done with an Excel, see [CABSPlan](./0_CABS_2AHIF_25_26.ods), Tab "Zeitplan".

The problem with Excel is that if lessons must be shifted, the shift must be rolled over holidays, event days etc, which makes it tedious.

The tool shall help me:

- Create a time axis similar to the excel.
- Mark holidays and test days.
- Keep a editable list of topics.
- Let me place topics onto the schedule
- On insertion, move the following topics across the schedule, moving past holidays and test days.
- On deletion, move the following topics backwards, moving past holidays and test days.
- Topics shall have a heading and an additional description.
- Should be graphically oriented, e.g. I want to drag the items around.
  
  ## Entity Analysis

  These are the main data entities of the application:

  - *Day*: can be Mo, Tue, Wed, ..., Sa, Sun. Can be marked as either "holiday" or "exam" or "event" or unmarked. Holds the calendar date and fiscal week.
  - *Course*: The subject that is taught. Has a Name and a Description. Holds a list of Topic. Occurs on certain weekdays (one or more, e.g. Tue and Fr).
  - *Topic*: Associated with a Course. This is the topic being presented on a certain Day. Has a Name and a Description. Can be placed on one or multiple Day.
  - *Exam*: A special kind of Topic. Has a Name only.
  - *Config*: Some static data:
    - Beginning of planning time
    - End of planning time
    - colours for Days marked holiday, exam or event.
    - other application conf

So there is a sequence of Day within the beginning and end of planning time (inclusive). All weekdays are present. A Course is placed on one or more weekdays, meaning there are lessons planned. On each such day, a Topic is presented. A list of Topic is available for planning. A Topic can be placed only on days of the Course. One Topic can be placed on multiple weeks. An Exam is a special Topic: It can be placed on any day and marks that day as "Exam".

## User Interface

- Angular Material style. 
- Vertical Time axis, horizontal weeks, with fiscal week number displayed.
- An area where an alphabetically sorted list of Topics can be managed (edit, change, remove).
- A drop-down to select the Course.
- The Topics shall be moved by drag and drop, if possible. Otherwise, select and move with buttons.

  ## Technical requirements

  - There is no need for user management.
  - Storage shall be in a relational DB backend.
  - The frontend shall be written in Angular.
  - There is no need for a backend server, the db connection and logic shall be implemented in the Angular frontend.
  - The app shall run in a docker stack.
 
