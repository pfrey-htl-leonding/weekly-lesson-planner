# Planner interaction wireframe

This Phase 0 wireframe records the agreed layout and interaction boundaries. It is not a finished visual design.

```text
┌──────────────────────────────── Weekly Lesson Planner ────────────────────────────────┐
│ Course [ CABS ▾ ]                  ☐ Edit shifts schedule                  [Export] │
├───────────────────────────────┬────────────────────────────────────────────────────────┤
│ Unplanned topics              │ ISO week │ Mon │ Tue │ Wed │ Thu │ Fri                 │
│ Search [________________]     │ W36      │ ... │ ... │ ... │ ... │ ...                 │
│                               │ W37      │ ... │ ... │ ... │ ... │ ...                 │
│  ┌ Topic A ────────────────┐  │ W38      │ ... │ ... │ ... │ ... │ ...                 │
│  │ description             │  │          global Holiday/Event spans the time axis      │
│  └─────────────────────────┘  │          course Exam is shown only for selected course │
│  ┌ Topic B ────────────────┐  │                                                        │
│  │ description             │  │  Scheduled card actions: Edit · Copy · Remove          │
│  └─────────────────────────┘  │                                                        │
└───────────────────────────────┴────────────────────────────────────────────────────────┘
```

## Drag/drop contract

- Pointer movement changes only the dragged card and valid-target highlight.
- No API call and no shifted-schedule preview occurs before drop.
- A cancelled or invalid drop makes no request.
- A valid list-to-calendar drop sends one placement command.
- A valid calendar-to-calendar drop sends one atomic drag command with both shift modes set from the combined checkbox.

## Fixed-day capacity feedback

- Adding a global holiday/event shifts every affected course as one atomic backend command.
- Adding an exam shifts only the selected course.
- If capacity is insufficient, show the backend conflict and keep the complete schedule unchanged.
