using System.Text.Json.Serialization;

namespace LupiraDavApi.Backends;

[JsonConverter(typeof(JsonStringEnumConverter<DavCollectionKind>))]
public enum DavCollectionKind { EventCalendar, TodoList, AddressBook }
