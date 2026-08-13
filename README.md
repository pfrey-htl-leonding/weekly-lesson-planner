# A Tool to plan lessons across the school year

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
 

