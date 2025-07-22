using System.ComponentModel.DataAnnotations;

namespace NetMediate.Tests.Messages;

internal record DecoupledValidatableMessage([Required] string Name);
