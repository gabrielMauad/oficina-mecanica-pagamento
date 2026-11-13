using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pagamento.Adapters.Controllers;
using Pagamento.Adapters.Models.Requests;
using Pagamento.Web.Extensions;

namespace Pagamento.Web.Controllers;

// [Authorize] na classe só exige usuário autenticado: múltiplos atributos [Authorize(Roles=...)]
// (classe + método) são combinados com E, não sobrescritos — por isso o papel é declarado
// explicitamente em cada ação.
[Authorize]
[ApiController]
[Route("api/v1/pagamentos/ordens-servico")]
public class PagamentoApiController : ControllerBase
{
    // Mantidos em sincronia com PagamentoErrors (Application é internal).
    private const string AcessoNegadoErrorCode = "Pagamento.AcessoNegado";
    private const string PendenteNaoEncontradoErrorCode = "Pagamento.PendenteNaoEncontrado";
    private const string ResolvidoPorOutraChamadaErrorCode = "Pagamento.ResolvidoPorOutraChamada";

    private readonly PagamentoController _caController;

    public PagamentoApiController(PagamentoController caController) => _caController = caController;

    [Authorize(Roles = "Cliente,Oficina")]
    [HttpGet("{ordemServicoId:guid}")]
    public async Task<IActionResult> ListarPorOrdemServico(Guid ordemServicoId)
    {
        var result = await _caController.ListarPorOrdemServico(ordemServicoId, User.ObterClienteIdDoSolicitante());

        if (result.IsFailure)
            return result.Error.Code == AcessoNegadoErrorCode
                ? StatusCode(StatusCodes.Status403Forbidden, result.Error)
                : UnprocessableEntity(result.Error);

        return Ok(result.Value);
    }

    [Authorize(Roles = "Cliente")]
    [HttpPost("{ordemServicoId:guid}/pagar")]
    public async Task<IActionResult> Pagar(Guid ordemServicoId, [FromBody] PagarRequest request)
    {
        var result = await _caController.Pagar(
            ordemServicoId, request, User.ObterClienteIdDoSolicitante() ?? Guid.Empty);

        if (result.IsFailure)
            return result.Error.Code switch
            {
                AcessoNegadoErrorCode => StatusCode(StatusCodes.Status403Forbidden, result.Error),
                PendenteNaoEncontradoErrorCode => NotFound(result.Error),
                ResolvidoPorOutraChamadaErrorCode => Conflict(result.Error),
                _ => UnprocessableEntity(result.Error)
            };

        return Ok(result.Value);
    }
}
