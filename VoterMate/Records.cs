namespace VoterMate;

internal record ContactCommitment(string Canvasser, string Mobilizer, string Friend, string Time, string Latitude, string Longitude)
{
    public ContactCommitment(string Canvasser, string Mobilizer, string Friend, double latitude, double longitude) :
        this(Canvasser, Mobilizer, Friend, DateTime.Now.ToString("MM/dd HH:mm:ss"), latitude.ToString("0.####"), longitude.ToString("0.####"))
    { }
}

internal record DoorKnock(string Canvasser, string Address, string Result, string Time)
{
    public DoorKnock(string Canvasser, string Address, string Result) :
        this(Canvasser, Address, Result, DateTime.Now.ToString("MM/dd HH:mm:ss"))
    { }
}

internal record PhoneNumber(string Canvasser, string Mobilizer, string? Phone, string Time, string Latitude, string Longitude, string? NewName)
{
    public PhoneNumber(string Canvasser, string Mobilizer, string? Phone, double latitude, double longitude, string? newName) :
        this(Canvasser, Mobilizer, Phone, DateTime.Now.ToString("MM/dd HH:mm:ss"), latitude.ToString("0.####"), longitude.ToString("0.####"), newName)
    { }
}

internal record TravelLog(string Canvasser, string? Action, string? Mobilizer, string Time, string Speed, string Latitude, string Longitude)
{
    public TravelLog(string Canvasser, string Action, string? Mobilizer, double? speed, double? latitude, double? longitude) :
        this(Canvasser, Action, Mobilizer, DateTime.Now.ToString("MM/dd HH:mm:ss"), speed?.ToString("0.##m/s") ?? "", latitude?.ToString("0.####") ?? "Unknown", longitude?.ToString("0.####") ?? "Unknown")
    { }
}
