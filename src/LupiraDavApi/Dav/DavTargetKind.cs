namespace LupiraDavApi.Dav;

/// <summary>Where in the unified tree a request points.</summary>
internal enum DavTargetKind
{
    Root,
    Principal,
    CalendarHome,
    AddressBookHome,
    Collection,
    Resource,
    Unknown,
}
