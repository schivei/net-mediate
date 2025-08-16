using System.ComponentModel.DataAnnotations;

namespace NetMediate.Tests.Messages;

[KeyedMessage("keyed2")]
internal record Keyed2Message([Required] string Name) : Keyed1Message(Name);
