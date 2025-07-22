using System.ComponentModel.DataAnnotations;

namespace NetMediate.Tests.Messages;

[KeyedMessage("vkeyed1")]
internal record Keyed1ValidatableMessage([Required] string Name) : SimpleValidatableMessage(Name);
