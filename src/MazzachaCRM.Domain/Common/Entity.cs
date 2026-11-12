namespace MazzachaCRM.Domain.Common;

/// <summary>Сущность с идентификатором, который создаётся в домене, а не в базе.</summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
}

/// <summary>Корень агрегата: единственная точка входа для изменения всего, что внутри него.</summary>
public abstract class AggregateRoot : Entity;
