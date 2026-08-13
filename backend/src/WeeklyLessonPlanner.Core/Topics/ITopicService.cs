namespace WeeklyLessonPlanner.Core.Topics;

public interface ITopicService
{
    Task<IReadOnlyList<TopicDto>> GetTopicsAsync(Guid? courseId, CancellationToken cancellationToken = default);
    Task<TopicDto?> GetTopicAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TopicDto> CreateTopicAsync(SaveTopicCommand command, CancellationToken cancellationToken = default);
    Task<TopicDto?> UpdateTopicAsync(Guid id, SaveTopicCommand command, CancellationToken cancellationToken = default);
    Task<bool> DeleteTopicAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TopicInstanceDto>> GetUnplannedInstancesAsync(
        Guid courseId,
        string? search,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteUnplannedInstanceAsync(Guid id, CancellationToken cancellationToken = default);
}
