# NetMediate

Uma biblioteca .NET moderna que implementa o padrão Mediator para facilitar a comunicação entre componentes de forma desacoplada e organizada.

## Características

- **Notifications**: Publique notificações para múltiplos handlers (padrão pub-sub)
- **Commands**: Execute comandos com um único handler
- **Requests**: Realize requisições que retornam respostas
- **Streams**: Trabalhe com fluxos de dados assíncronos
- **Validation**: Valide mensagens automaticamente antes do processamento
- **Dependency Injection**: Integração nativa com Microsoft.Extensions.DependencyInjection
- **Keyed Services**: Suporte para serviços com chaves para cenários avançados

## Instalação

```bash
dotnet add package NetMediate
```

## Configuração Inicial

### 1. Registrar os Serviços

No seu `Program.cs` ou `Startup.cs`:

```csharp
using NetMediate;

// Registra NetMediate escaneando todos os assemblies carregados
services.AddNetMediate();

// Ou escaneie apenas assemblies específicos
services.AddNetMediate(typeof(Program).Assembly);

// Ou múltiplos assemblies
services.AddNetMediate(assembly1, assembly2, assembly3);
```

### 2. Injetar o IMediator

```csharp
public class MeuController : ControllerBase
{
    private readonly IMediator _mediator;

    public MeuController(IMediator mediator)
    {
        _mediator = mediator;
    }
}
```

## 1. Notifications - Notificações

As notificações permitem que múltiplos handlers sejam executados quando uma mensagem é publicada.

### Definindo uma Notificação

```csharp
public record UsuarioCriadoNotificacao(int UsuarioId, string Nome, string Email);
```

### Criando Handlers de Notificação

```csharp
public class EnviarEmailHandler : INotificationHandler<UsuarioCriadoNotificacao>
{
    private readonly IEmailService _emailService;
    
    public EnviarEmailHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public async Task Handle(UsuarioCriadoNotificacao notification, CancellationToken cancellationToken = default)
    {
        await _emailService.EnviarEmailBoasVindas(notification.Email, notification.Nome);
    }
}

public class AtualizarCacheHandler : INotificationHandler<UsuarioCriadoNotificacao>
{
    private readonly ICacheService _cache;
    
    public AtualizarCacheHandler(ICacheService cache)
    {
        _cache = cache;
    }

    public async Task Handle(UsuarioCriadoNotificacao notification, CancellationToken cancellationToken = default)
    {
        await _cache.InvalidarCache($"usuario:{notification.UsuarioId}");
    }
}
```

### Enviando Notificações

```csharp
public class UsuarioService
{
    private readonly IMediator _mediator;

    public UsuarioService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task CriarUsuario(string nome, string email)
    {
        var usuario = new Usuario { Nome = nome, Email = email };
        // ... salvar usuário no banco de dados

        // Notifica todos os handlers interessados
        await _mediator.Notify(new UsuarioCriadoNotificacao(usuario.Id, usuario.Nome, usuario.Email));
    }
}
```

## 2. Commands - Comandos

Os comandos são processados por um único handler e não retornam valores.

### Definindo um Comando

```csharp
public record CriarUsuarioCommand(string Nome, string Email, DateTime DataNascimento);
```

### Criando um Handler de Comando

```csharp
public class CriarUsuarioCommandHandler : ICommandHandler<CriarUsuarioCommand>
{
    private readonly IUsuarioRepository _repository;
    private readonly IValidator<CriarUsuarioCommand> _validator;

    public CriarUsuarioCommandHandler(IUsuarioRepository repository, IValidator<CriarUsuarioCommand> validator)
    {
        _repository = repository;
        _validator = validator;
    }

    public async Task Handle(CriarUsuarioCommand command, CancellationToken cancellationToken = default)
    {
        // Validação personalizada (se necessário)
        var resultado = await _validator.ValidateAsync(command);
        if (!resultado.IsValid)
            throw new ValidationException(resultado.Errors.First().ErrorMessage);

        var usuario = new Usuario
        {
            Nome = command.Nome,
            Email = command.Email,
            DataNascimento = command.DataNascimento,
            DataCriacao = DateTime.UtcNow
        };

        await _repository.SalvarAsync(usuario);
    }
}
```

### Executando Comandos

```csharp
public class UsuarioController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsuarioController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> CriarUsuario([FromBody] CriarUsuarioRequest request)
    {
        var command = new CriarUsuarioCommand(request.Nome, request.Email, request.DataNascimento);
        
        await _mediator.Send(command);
        
        return Ok(new { Mensagem = "Usuário criado com sucesso!" });
    }
}
```

## 3. Requests - Requisições

As requisições retornam uma resposta específica.

### Definindo Requisição e Resposta

```csharp
public record ObterUsuarioQuery(int UsuarioId);

public record UsuarioResponse(int Id, string Nome, string Email, DateTime DataCriacao);
```

### Criando um Handler de Requisição

```csharp
public class ObterUsuarioQueryHandler : IRequestHandler<ObterUsuarioQuery, UsuarioResponse>
{
    private readonly IUsuarioRepository _repository;

    public ObterUsuarioQueryHandler(IUsuarioRepository repository)
    {
        _repository = repository;
    }

    public async Task<UsuarioResponse> Handle(ObterUsuarioQuery query, CancellationToken cancellationToken = default)
    {
        var usuario = await _repository.ObterPorIdAsync(query.UsuarioId);
        
        if (usuario == null)
            throw new NotFoundException($"Usuário com ID {query.UsuarioId} não encontrado");

        return new UsuarioResponse(
            usuario.Id, 
            usuario.Nome, 
            usuario.Email, 
            usuario.DataCriacao
        );
    }
}
```

### Fazendo Requisições

```csharp
public class UsuarioController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsuarioController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UsuarioResponse>> ObterUsuario(int id)
    {
        var query = new ObterUsuarioQuery(id);
        var usuario = await _mediator.Request<ObterUsuarioQuery, UsuarioResponse>(query);
        
        return Ok(usuario);
    }
}
```

## 4. Streams - Fluxos de Dados

Os streams permitem retornar múltiplos valores de forma assíncrona.

### Definindo Stream Query

```csharp
public record ObterUsuariosPaginadosQuery(int Pagina, int TamanhoPagina, string? Filtro);
```

### Criando um Handler de Stream

```csharp
public class ObterUsuariosPaginadosQueryHandler : IStreamHandler<ObterUsuariosPaginadosQuery, UsuarioResponse>
{
    private readonly IUsuarioRepository _repository;

    public ObterUsuariosPaginadosQueryHandler(IUsuarioRepository repository)
    {
        _repository = repository;
    }

    public async IAsyncEnumerable<UsuarioResponse> Handle(
        ObterUsuariosPaginadosQuery query, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var skip = (query.Pagina - 1) * query.TamanhoPagina;
        
        await foreach (var usuario in _repository.ObterUsuariosPaginadosAsync(skip, query.TamanhoPagina, query.Filtro))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new UsuarioResponse(
                usuario.Id,
                usuario.Nome,
                usuario.Email,
                usuario.DataCriacao
            );
        }
    }
}
```

### Consumindo Streams

```csharp
public class UsuarioController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsuarioController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> ListarUsuarios(int pagina = 1, int tamanho = 10, string? filtro = null)
    {
        var query = new ObterUsuariosPaginadosQuery(pagina, tamanho, filtro);
        var usuarios = new List<UsuarioResponse>();

        await foreach (var usuario in _mediator.RequestStream<ObterUsuariosPaginadosQuery, UsuarioResponse>(query))
        {
            usuarios.Add(usuario);
        }

        return Ok(usuarios);
    }
}
```

## 5. Validation - Validações

O NetMediate suporta validação automática de mensagens antes do processamento.

### Validação com IValidatable

```csharp
public record CriarProdutoCommand(string Nome, decimal Preco, int CategoriaId) : IValidatable
{
    public Task<ValidationResult> ValidateAsync()
    {
        var errors = new List<ValidationResult>();

        if (string.IsNullOrWhiteSpace(Nome))
            errors.Add(new ValidationResult("Nome é obrigatório", [nameof(Nome)]));
        
        if (Preco <= 0)
            errors.Add(new ValidationResult("Preço deve ser maior que zero", [nameof(Preco)]));
        
        if (CategoriaId <= 0)
            errors.Add(new ValidationResult("Categoria é obrigatória", [nameof(CategoriaId)]));

        if (errors.Count > 0)
            return Task.FromResult(new ValidationResult(string.Join("; ", errors.Select(e => e.ErrorMessage))));

        return Task.FromResult(ValidationResult.Success!);
    }
}
```

### Validação com IValidationHandler

```csharp
public class CriarProdutoValidationHandler : IValidationHandler<CriarProdutoCommand>
{
    private readonly ICategoriaRepository _categoriaRepository;

    public CriarProdutoValidationHandler(ICategoriaRepository categoriaRepository)
    {
        _categoriaRepository = categoriaRepository;
    }

    public async ValueTask<ValidationResult> ValidateAsync(CriarProdutoCommand message, CancellationToken cancellationToken = default)
    {
        // Validações que requerem acesso ao banco de dados ou serviços externos
        var categoriaExiste = await _categoriaRepository.ExisteAsync(message.CategoriaId);
        
        if (!categoriaExiste)
            return new ValidationResult($"Categoria com ID {message.CategoriaId} não existe");

        return ValidationResult.Success!;
    }
}
```

### Validação com Data Annotations

```csharp
public record AtualizarUsuarioCommand(
    [Required] int Id,
    [Required, StringLength(100)] string Nome,
    [Required, EmailAddress] string Email,
    [Range(18, 120)] int Idade
);
```

### Handler com Validação Automática

```csharp
public class AtualizarUsuarioCommandHandler : ICommandHandler<AtualizarUsuarioCommand>
{
    private readonly IUsuarioRepository _repository;

    public AtualizarUsuarioCommandHandler(IUsuarioRepository repository)
    {
        _repository = repository;
    }

    public async Task Handle(AtualizarUsuarioCommand command, CancellationToken cancellationToken = default)
    {
        // As validações já foram executadas automaticamente pelo NetMediate
        
        var usuario = await _repository.ObterPorIdAsync(command.Id);
        if (usuario == null)
            throw new NotFoundException($"Usuário com ID {command.Id} não encontrado");

        usuario.Nome = command.Nome;
        usuario.Email = command.Email;
        usuario.Idade = command.Idade;

        await _repository.AtualizarAsync(usuario);
    }
}
```

## Recursos Avançados

### Serviços com Chaves (Keyed Services)

Use o atributo `[KeyedMessage]` para direcionar mensagens para handlers específicos:

```csharp
[KeyedMessage("relatorio-vendas")]
public record GerarRelatorioVendasCommand(DateTime DataInicio, DateTime DataFim);

[KeyedMessage("relatorio-estoque")]
public record GerarRelatorioEstoqueCommand(int CategoriaId);
```

### Configuração de Comportamentos

```csharp
services.AddNetMediate()
    .IgnoreUnhandledMessages()  // Ignora mensagens sem handlers
    .LogUnhandledMessages()     // Registra mensagens não tratadas nos logs
    .FilterNotification<MinhaNotificacao, MeuHandler>(msg => msg.Ativo) // Filtra notificações
    .FilterCommand<MeuCommand, MeuCommandHandler>(cmd => cmd.Valido);    // Filtra comandos
```

## Tratamento de Erros

```csharp
public class UsuarioController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsuarioController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> CriarUsuario([FromBody] CriarUsuarioCommand command)
    {
        try
        {
            await _mediator.Send(command);
            return Ok(new { Sucesso = true });
        }
        catch (MessageValidationException ex)
        {
            return BadRequest(new { Erro = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Erro = "Erro interno do servidor" });
        }
    }
}
```

## Boas Práticas

1. **Use records** para mensagens imutáveis
2. **Mantenha handlers focados** em uma única responsabilidade
3. **Implemente validação** tanto no nível de atributos quanto em handlers personalizados
4. **Use CancellationToken** para operações que podem ser canceladas
5. **Trate erros adequadamente** em cada handler
6. **Use keyed services** apenas quando necessário para cenários avançados
7. **Monitore performance** em handlers de stream com grandes volumes de dados

## Exemplos Completos

Veja a pasta `tests/NetMediate.Tests/` no repositório para exemplos completos de implementação de todos os tipos de handlers.

## Contribuição

Contribuições são bem-vindas! Por favor, abra uma issue ou pull request no [repositório do GitHub](https://github.com/schivei/net-mediate).

## Licença

Este projeto está licenciado sob a [Licença MIT](LICENSE).
