using System.ComponentModel.DataAnnotations;

namespace NetMediate.Tests.Messages;

[KeyedMessage("vkeyed2")]
internal record Keyed2ValidatableMessage([Required] string Name) : Keyed1ValidatableMessage(Name);
