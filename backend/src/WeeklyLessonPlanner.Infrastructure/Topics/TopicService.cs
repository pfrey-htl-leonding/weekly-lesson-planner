using Microsoft.EntityFrameworkCore;
using WeeklyLessonPlanner.Core.Planning;
using WeeklyLessonPlanner.Core.Topics;
using WeeklyLessonPlanner.Infrastructure.Persistence;

namespace WeeklyLessonPlanner.Infrastructure.Topics;

public sealed class TopicService(PlannerDbContext dbContext) : ITopicService
{
    public async Task<IReadOnlyList<TopicDto>> GetTopicsAsync(
        Guid? courseId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Topics.AsNoTracking().AsQueryable();
        if (courseId.HasValue)
        {
            query = query.Where(item => item.CourseId == courseId.Value);
        }

        return await Project(query.OrderBy(item => item.Heading.ToLower()).ThenBy(item => item.Heading))
            .ToListAsync(cancellationToken);
    }

    public Task<TopicDto?> GetTopicAsync(Guid id, CancellationToken cancellationToken = default) =>
        Project(dbContext.Topics.AsNoTracking().Where(item => item.Id == id))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<TopicDto> CreateTopicAsync(
        SaveTopicCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        if (!await dbContext.Courses.AnyAsync(item => item.Id == command.CourseId, cancellationToken))
        {
            throw new KeyNotFoundException("Course not found.");
        }

        var topic = new Topic
        {
            Id = Guid.NewGuid(),
            CourseId = command.CourseId,
            Heading = command.Heading.Trim(),
            Description = command.Description?.Trim() ?? string.Empty
        };
        topic.Instances.Add(new TopicInstance
        {
            Id = Guid.NewGuid(),
            CourseId = command.CourseId
        });

        dbContext.Topics.Add(topic);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await GetTopicAsync(topic.Id, cancellationToken))!;
    }

    public async Task<TopicDto?> UpdateTopicAsync(
        Guid id,
        SaveTopicCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        var topic = await dbContext.Topics.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (topic is null)
        {
            return null;
        }

        if (topic.CourseId != command.CourseId)
        {
            throw new PlanningConflictException("A topic definition cannot be moved to another course.");
        }

        topic.Heading = command.Heading.Trim();
        topic.Description = command.Description?.Trim() ?? string.Empty;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetTopicAsync(topic.Id, cancellationToken);
    }

    public async Task<bool> DeleteTopicAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var topic = await dbContext.Topics
            .Include(item => item.Instances)
            .ThenInclude(item => item.Assignment)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (topic is null)
        {
            return false;
        }

        if (topic.Instances.Any(item => item.Assignment is not null))
        {
            throw new PlanningConflictException(
                "A topic definition cannot be deleted while one or more instances are scheduled.");
        }

        dbContext.Topics.Remove(topic);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<TopicInstanceDto>> GetUnplannedInstancesAsync(
        Guid courseId,
        string? search,
        CancellationToken cancellationToken = default)
    {
        if (!await dbContext.Courses.AnyAsync(item => item.Id == courseId, cancellationToken))
        {
            throw new KeyNotFoundException("Course not found.");
        }

        var query = dbContext.TopicInstances
            .AsNoTracking()
            .Where(item => item.CourseId == courseId && item.Assignment == null);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(item =>
                EF.Functions.ILike(item.Topic.Heading, pattern) ||
                EF.Functions.ILike(item.Topic.Description, pattern));
        }

        return await query
            .OrderBy(item => item.Topic.Heading.ToLower())
            .ThenBy(item => item.Topic.Heading)
            .ThenBy(item => item.Id)
            .Select(item => new TopicInstanceDto(
                item.Id,
                item.TopicId,
                item.CourseId,
                item.Topic.Heading,
                item.Topic.Description))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteUnplannedInstanceAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var instance = await dbContext.TopicInstances
            .Include(item => item.Assignment)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (instance is null)
        {
            return false;
        }

        if (instance.Assignment is not null)
        {
            throw new PlanningConflictException(
                "A scheduled topic instance must be removed through a planning command.");
        }

        dbContext.TopicInstances.Remove(instance);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static IQueryable<TopicDto> Project(IQueryable<Topic> query) => query.Select(item => new TopicDto(
        item.Id,
        item.CourseId,
        item.Heading,
        item.Description,
        item.Instances.Count,
        item.Instances.Count(instance => instance.Assignment != null),
        item.Instances.Count(instance => instance.Assignment == null)));

    private static void Validate(SaveTopicCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Heading) || command.Heading.Trim().Length > 200)
        {
            throw new ArgumentException("Topic heading is required and must not exceed 200 characters.");
        }

        if (command.Description?.Length > 4000)
        {
            throw new ArgumentException("Topic description must not exceed 4000 characters.");
        }
    }
}
