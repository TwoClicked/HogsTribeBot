using TribeBot.Core.Entities;
using TribeBot.Core.Interfaces;
using TribeBot.Data.Interfaces;

public class DeliveryEventService : IDeliveryEventService
{
    private string? _activeEventId;
    private DateTime _startUtc;

    private readonly IGoogleSheetsDataStore _dataStore;
    private readonly IMemberService _memberService;
    private readonly IFineService _fineService;

    public DeliveryEventService(
        IGoogleSheetsDataStore datastore,
        IMemberService memberService,
        IFineService fineService)
    {
        _dataStore = datastore;
        _memberService = memberService;
        _fineService = fineService;
    }

    // ============================================================
    // START EVENT
    // ============================================================
    public async Task<string> StartEventAsync()
    {
        _activeEventId = $"delivery_{DateTime.UtcNow:yyyyMMdd_HHmm}";
        _startUtc = DateTime.UtcNow;

        await _dataStore.AddDeliveryEventAsync(_activeEventId, _startUtc);

        return _activeEventId;
    }

    // ============================================================
    // LEGACY
    // ============================================================
    public string? GetActiveEventId()
    {
        return _activeEventId;
    }

    // ============================================================
    // GET ACTIVE (PERSISTENT SAFE)
    // ============================================================
    public async Task<string?> GetActiveEventIdAsync()
    {
        if (!string.IsNullOrWhiteSpace(_activeEventId))
            return _activeEventId;

        var events = await _dataStore.GetAllDeliveryEventsAsync();

        var active = events.FirstOrDefault(e => e.IsActive);

        if (string.IsNullOrWhiteSpace(active.EventId))
            return null;

        _activeEventId = active.EventId;
        _startUtc = active.StartUtc;

        return _activeEventId;
    }

    // ============================================================
    // REGISTER BRACELET
    // ============================================================
    public async Task RegisterBraceletAsync(Member member, int amount)
    {
        var eventId = await GetActiveEventIdAsync();
        if (eventId == null) return;

        var entries = await _dataStore.GetDeliveryEntriesByEventAsync(eventId);

        bool alreadySubmitted = entries.Any(e =>
            !string.IsNullOrWhiteSpace(e.DiscordUserId) &&
            e.DiscordUserId.Trim() == member.DiscordUserId.Trim());

        if (alreadySubmitted)
            return;

        await _dataStore.AddDeliveryEntryAsync(new DeliveryEntry
        {
            EventId = eventId,
            DiscordUserId = member.DiscordUserId,
            IngameName = member.IngameName,
            SubmissionType = "Bracelet",
            Amount = amount,
            TimestampUtc = DateTime.UtcNow
        });
    }

    // ============================================================
    // REGISTER GOLD
    // ============================================================
    public async Task RegisterGoldAsync(Member member, int amount)
    {
        var eventId = await GetActiveEventIdAsync();
        if (eventId == null) return;

        var entries = await _dataStore.GetDeliveryEntriesByEventAsync(eventId);

        bool alreadySubmitted = entries.Any(e =>
            !string.IsNullOrWhiteSpace(e.DiscordUserId) &&
            e.DiscordUserId.Trim() == member.DiscordUserId.Trim());

        if (alreadySubmitted)
            return;

        await _dataStore.AddDeliveryEntryAsync(new DeliveryEntry
        {
            EventId = eventId,
            DiscordUserId = member.DiscordUserId,
            IngameName = member.IngameName,
            SubmissionType = "Gold",
            Amount = amount,
            TimestampUtc = DateTime.UtcNow
        });
    }

    // ============================================================
    // CHECK USER PARTICIPATION (🔥 REQUIRED FOR !checkdelivery)
    // ============================================================
    public async Task<bool> HasUserParticipatedAsync(string eventId, string discordUserId)
    {
        var entries = await _dataStore.GetDeliveryEntriesByEventAsync(eventId);

        return entries.Any(e =>
            !string.IsNullOrWhiteSpace(e.DiscordUserId) &&
            e.DiscordUserId.Trim() == discordUserId.Trim());
    }

    // ============================================================
    // GET NON PARTICIPANTS
    // ============================================================
    public async Task<List<string>> GetNonParticipantsAsync(string eventId)
    {
        var members = await _memberService.GetAllMembersAsync();
        var entries = await _dataStore.GetDeliveryEntriesByEventAsync(eventId);

        var compliant = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.DiscordUserId))
            .Select(e => e.DiscordUserId.Trim())
            .ToHashSet();

        var missing = members
            .Where(m => !m.DeliveryExempt)
            .Where(m => !compliant.Contains(m.DiscordUserId.Trim()))
            .Select(m => m.IngameName)
            .ToList();

        return missing;
    }

    // ============================================================
    // END EVENT + FINES
    // ============================================================
    public async Task<List<Member>> EndEventAsync(string eventId)
    {
        var members = await _memberService.GetAllMembersAsync();
        var entries = await _dataStore.GetDeliveryEntriesByEventAsync(eventId);

        var compliant = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.DiscordUserId))
            .Select(e => e.DiscordUserId.Trim())
            .ToHashSet();

        var missing = members
            .Where(m => !m.DeliveryExempt)
            .Where(m => !compliant.Contains(m.DiscordUserId.Trim()))
            .ToList();

        foreach (var m in missing)
        {
            await _fineService.AddEventFineAsync(
                m,
                150_000_000,
                $"Missed Delivery Event ({eventId})"
            );
        }

        await _dataStore.EndDeliveryEventAsync(eventId);

        _activeEventId = null;

        return missing; // 🔥 RETURN WHO GOT FINED
    }
}