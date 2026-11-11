using System.Diagnostics.Metrics;

namespace SharedKernel.Application.Metrics;

public sealed class IntegracoesMetrics : IDisposable
{
    public const string MeterName = "OficinaMecanica.Integracoes";
    public const string FalhasInstrumentName = "oficina.integracoes.falhas";

    private readonly Meter _meter;
    private readonly Counter<long> _falhas;

    public IntegracoesMetrics()
    {
        _meter = new Meter(MeterName);

        _falhas = _meter.CreateCounter<long>(
            FalhasInstrumentName,
            unit: "{falha}",
            description: "Falhas no processamento de eventos de integração");
    }

    public Meter Meter => _meter;

    public void RegistrarFalha(string evento, string handler) =>
        _falhas.Add(
            1,
            new KeyValuePair<string, object?>("evento", evento),
            new KeyValuePair<string, object?>("handler", handler));

    public void Dispose() => _meter.Dispose();
}
