using System.ComponentModel.DataAnnotations;

namespace NetMediate.Tests.Messages;

[KeyedMessage("keyed1")]
internal record Keyed1Message([Required] string Name) : SimpleMessage(Name);
