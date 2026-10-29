using System.Data;
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

    public async Task<ImportTopicCatalogResultDto> ImportTopicCatalogAsync(
        ImportTopicCatalogCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Topics.Count == 0)
        {
            throw new ArgumentException("The topic import contains no topics.");
        }

        foreach (var item in command.Topics)
        {
            Validate(new SaveTopicCommand(command.CourseId, item.Heading, item.Description));
        }

        var duplicate = command.Topics
            .GroupBy(item => item.Heading.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"The import contains the topic heading '{duplicate}' more than once.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await dbContext.Courses.AnyAsync(
                item => item.Id == command.CourseId, cancellationToken))
        {
            throw new KeyNotFoundException("Course not found.");
        }

        var existingTopics = await dbContext.Topics
            .Where(item => item.CourseId == command.CourseId)
            .ToListAsync(cancellationToken);
        var updatedCount = 0;

        foreach (var imported in command.Topics)
        {
            var heading = imported.Heading.Trim();
            var matches = existingTopics
                .Where(item => string.Equals(
                    item.Heading.Trim(), heading, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length > 1)
            {
                throw new PlanningConflictException(
                    $"More than one existing topic is named '{heading}'.");
            }

            if (matches.Length == 1)
            {
                matches[0].Heading = heading;
                matches[0].Description = imported.Description?.Trim() ?? string.Empty;
                updatedCount += 1;
                continue;
            }

            var topic = new Topic
            {
                Id = Guid.NewGuid(),
                CourseId = command.CourseId,
                Heading = heading,
                Description = imported.Description?.Trim() ?? string.Empty
            };
            topic.Instances.Add(new TopicInstance
            {
                Id = Guid.NewGuid(),
                CourseId = command.CourseId
            });
            dbContext.Topics.Add(topic);
            existingTopics.Add(topic);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new PlanningConflictException(
                "The topic catalog changed concurrently. Refresh it and try the import again.",
                exception);
        }

        return new(
            command.Topics.Count,
            updatedCount,
            command.Topics.Count - updatedCount);
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
