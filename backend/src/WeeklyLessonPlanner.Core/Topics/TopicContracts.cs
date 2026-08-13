namespace WeeklyLessonPlanner.Core.Topics;

public sealed record TopicDto(
    Guid Id,
    Guid CourseId,
    string Heading,
    string Description,
    int TotalInstanceCount,
    int PlannedInstanceCount,
    int UnplannedInstanceCount);

public sealed record SaveTopicCommand(Guid CourseId, string Heading, string Description);

public sealed record TopicInstanceDto(
    Guid Id,
    Guid TopicId,
    Guid CourseId,
    string Heading,
    string Description);
