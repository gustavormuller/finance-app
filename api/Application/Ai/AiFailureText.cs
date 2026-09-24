namespace Finance.Api.Application.Ai;

/// <summary>
/// What a person reads when an AI call is refused or fails: the problem details' text and
/// <c>AiAnalysis.Error</c>. pt-BR, never an exception's message, which is English and may
/// name the model.
/// </summary>
public static class AiFailureText
{
    public const string Disabled = "A IA está desligada na sua conta. Ligue-a nas configurações para usar este recurso.";

    public const string BudgetExceeded = "Você atingiu o limite mensal de gastos com IA. O limite renova no próximo mês.";

    public const string Timeout = "O serviço de IA não respondeu a tempo. Tente novamente em instantes.";

    public const string ProviderFailed = "O serviço de IA não conseguiu responder agora. Tente novamente mais tarde.";

    public const string Unexpected = "Não foi possível gerar a análise. Tente novamente mais tarde.";

    public const string Interrupted = "A geração da análise foi interrompida. Gere a análise novamente.";

    /// <summary>The text for a failure; a timeout before its base class, and anything unforeseen as <see cref="Unexpected"/>.</summary>
    public static string For(Exception failure) => failure switch
    {
        AiDisabledException => Disabled,
        AiBudgetExceededException => BudgetExceeded,
        AiProviderTimeoutException => Timeout,
        AiProviderException => ProviderFailed,
        _ => Unexpected,
    };
}
