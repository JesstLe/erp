using Erp.Domain.Common;

namespace Erp.Domain.Customers;

public sealed class ServiceRecord : Entity
{
    private readonly List<ServiceRecordAttachment> _attachments = [];

    private ServiceRecord()
    {
    }

    public ServiceRecord(Guid tenantId, Guid storeId, Guid customerId, Guid? serviceOrderId,
        DateTimeOffset serviceOccurredAtUtc, string? conditionNotes, string? serviceContent,
        string? followUpNotes, Guid commandId, Guid createdBy, DateTimeOffset now,
        Guid? categoryId = null, Guid? caregiverEmployeeId = null,
        string? caregiverNameSnapshot = null, DateTimeOffset? followUpAtUtc = null) : base(tenantId)
    {
        if (storeId == Guid.Empty || customerId == Guid.Empty || commandId == Guid.Empty || createdBy == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "服务记录的门店、顾客、请求号和建档人不能为空");
        if (serviceOccurredAtUtc > now.AddMinutes(5))
            throw new DomainRuleException("VALIDATION_FAILED", "服务时间不能晚于当前时间");

        StoreId = storeId;
        CustomerId = customerId;
        ServiceOrderId = serviceOrderId;
        CategoryId = categoryId;
        ServiceOccurredAtUtc = serviceOccurredAtUtc;
        ConditionNotes = NormalizeOptional(conditionNotes, 2_000, "本次情况/需求");
        ServiceContent = NormalizeOptional(serviceContent, 4_000, "服务过程与内容");
        FollowUpNotes = NormalizeOptional(followUpNotes, 2_000, "结果与后续建议");
        SetCaregiver(caregiverEmployeeId, caregiverNameSnapshot);
        if (followUpAtUtc.HasValue && followUpAtUtc.Value < serviceOccurredAtUtc)
            throw new DomainRuleException("VALIDATION_FAILED", "回访提醒时间不能早于服务时间");
        FollowUpAtUtc = followUpAtUtc;
        CommandId = commandId;
        CreatedBy = createdBy;
    }

    public Guid StoreId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? ServiceOrderId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public DateTimeOffset ServiceOccurredAtUtc { get; private set; }
    public string? ConditionNotes { get; private set; }
    public string? ServiceContent { get; private set; }
    public string? FollowUpNotes { get; private set; }
    public Guid? CaregiverEmployeeId { get; private set; }
    public string? CaregiverNameSnapshot { get; private set; }
    public DateTimeOffset? FollowUpAtUtc { get; private set; }
    public Guid CommandId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public IReadOnlyCollection<ServiceRecordAttachment> Attachments => _attachments;

    private void SetCaregiver(Guid? employeeId, string? name)
    {
        if (employeeId.HasValue != !string.IsNullOrWhiteSpace(name))
            throw new DomainRuleException("VALIDATION_FAILED", "护理老师信息不完整");
        CaregiverEmployeeId = employeeId;
        CaregiverNameSnapshot = NormalizeOptional(name, 100, "护理老师");
    }

    public void AttachImage(Guid fileId)
    {
        if (fileId == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "服务记录图片文件无效");
        if (_attachments.Count >= 6)
            throw new DomainRuleException("VALIDATION_FAILED", "每条服务记录最多上传6张图片");
        if (_attachments.Any(x => x.FileId == fileId))
            throw new DomainRuleException("VALIDATION_FAILED", "同一图片不能重复添加");
        _attachments.Add(new ServiceRecordAttachment(TenantId, Id, fileId, _attachments.Count));
    }

    private static string? NormalizeOptional(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}最多{maxLength}个字符");
        return normalized;
    }
}

public sealed class ServiceRecordAttachment : Entity
{
    private ServiceRecordAttachment()
    {
    }

    internal ServiceRecordAttachment(Guid tenantId, Guid serviceRecordId, Guid fileId, int sortOrder) : base(tenantId)
    {
        ServiceRecordId = serviceRecordId;
        FileId = fileId;
        SortOrder = sortOrder;
    }

    public Guid ServiceRecordId { get; private set; }
    public Guid FileId { get; private set; }
    public int SortOrder { get; private set; }
}

public sealed class ServiceRecordCorrection : Entity
{
    private ServiceRecordCorrection() { }

    public ServiceRecordCorrection(Guid tenantId, Guid serviceRecordId, string? reason, string? conditionNotes,
        string? serviceContent, string? followUpNotes, Guid? caregiverEmployeeId,
        string? caregiverNameSnapshot, DateTimeOffset? followUpAtUtc, Guid commandId,
        Guid correctedBy) : base(tenantId)
    {
        if (serviceRecordId == Guid.Empty || commandId == Guid.Empty || correctedBy == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "更正记录、请求号和更正人不能为空");
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "日常补充更新" : reason.Trim();
        if (normalizedReason.Length > 500)
            throw new DomainRuleException("VALIDATION_FAILED", "更正说明最多500字");
        ServiceRecordId = serviceRecordId;
        Reason = normalizedReason;
        ConditionNotes = NormalizeOptional(conditionNotes, 2_000, "更正后的本次情况/需求");
        ServiceContent = NormalizeOptional(serviceContent, 4_000, "更正后的服务过程与内容");
        FollowUpNotes = NormalizeOptional(followUpNotes, 2_000, "更正后的结果与后续建议");
        if (caregiverEmployeeId.HasValue != !string.IsNullOrWhiteSpace(caregiverNameSnapshot))
            throw new DomainRuleException("VALIDATION_FAILED", "护理老师信息不完整");
        CaregiverEmployeeId = caregiverEmployeeId;
        CaregiverNameSnapshot = NormalizeOptional(caregiverNameSnapshot, 100, "护理老师");
        FollowUpAtUtc = followUpAtUtc;
        CommandId = commandId;
        CorrectedBy = correctedBy;
    }

    public Guid ServiceRecordId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? ConditionNotes { get; private set; }
    public string? ServiceContent { get; private set; }
    public string? FollowUpNotes { get; private set; }
    public Guid? CaregiverEmployeeId { get; private set; }
    public string? CaregiverNameSnapshot { get; private set; }
    public DateTimeOffset? FollowUpAtUtc { get; private set; }
    public Guid CommandId { get; private set; }
    public Guid CorrectedBy { get; private set; }

    private static string? NormalizeOptional(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}最多{maxLength}个字符");
        return normalized;
    }
}
