import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { api } from '@/api/finance';
import { useMe } from '@/auth/useMe';
import Alert from '@/components/Alert';
import SectionHeading from '@/components/dashboard/SectionHeading';
import { formatMoney } from '@/lib/money';
import { formatMonth } from '@/lib/months';

/**
 * `/settings` (009): the user's own AI switch, what switching it on sends and to whom
 * (decision 10), and this month's spend against the cap (ADR-008).
 *
 * The disclosure lists exactly what 009's two features put in a request (DEFERRED, 009
 * CP4a and CP5a). It is copy, not derived from code, so a change to either request must
 * change it too.
 */
export default function SettingsPage() {
  const queryClient = useQueryClient();
  const me = useMe();
  // The API's default month is the one the budget counts in (UTC-3), so none is sent.
  const usage = useQuery({ queryKey: ['ai', 'usage'], queryFn: api.aiUsage });

  // Where the switch is going, set in the click handler itself. The mutation's own pending
  // state reaches the page a tick later, and in that tick the controlled switch went back.
  const [saving, setSaving] = useState<boolean | null>(null);

  const toggle = useMutation({
    mutationFn: (aiEnabled: boolean) => api.updateMe({ aiEnabled }),
    // The answer is the whole of `me`, so every page reading the flag sees it at once.
    onSuccess: (user) => queryClient.setQueryData(['me'], user),
    onSettled: () => setSaving(null),
  });

  // RequireAuth renders pages only once `me` has resolved to a user. While the PATCH is
  // in flight the switch shows where it is going; a refusal puts it back.
  const aiEnabled = saving ?? me.data?.aiEnabled ?? false;

  return (
    <section className="mx-auto max-w-3xl px-4 py-8">
      <h2 className="mb-6 text-2xl font-semibold tracking-tight">Configurações</h2>

      <div className="grid gap-8">
        <section aria-labelledby="ai-heading" className="grid gap-4">
          <SectionHeading id="ai-heading">Inteligência artificial</SectionHeading>

          {toggle.isError && <Alert>{toggle.error.message}</Alert>}

          <div className="flex items-center justify-between gap-4 rounded-lg border p-4">
            <div>
              <label htmlFor="ai-enabled" className="text-sm font-medium">
                Usar IA nesta conta
              </label>
              <p className="text-muted-foreground mt-1 text-sm">
                Liga "Sugerir com IA" na importação e a "Análise do mês" no painel.
              </p>
            </div>

            <div className="flex items-center gap-2">
              <span data-testid="ai-state" className="text-muted-foreground text-sm">
                {aiEnabled ? 'Ligada' : 'Desligada'}
              </span>
              <input
                id="ai-enabled"
                type="checkbox"
                role="switch"
                className="accent-primary size-5"
                checked={aiEnabled}
                disabled={saving !== null}
                onChange={(event) => {
                  setSaving(event.target.checked);
                  toggle.mutate(event.target.checked);
                }}
              />
            </div>
          </div>

          <Disclosure />
        </section>

        <section aria-labelledby="ai-spend-heading" className="grid gap-2">
          <SectionHeading id="ai-spend-heading">Gasto com IA neste mês</SectionHeading>

          {usage.isError && <Alert>Não foi possível carregar o gasto com IA.</Alert>}

          {usage.data && (
            <>
              <p data-testid="ai-spend" className="text-sm">
                <span className="font-semibold">{formatMoney(usage.data.spentBrl)}</span> de{' '}
                {formatMoney(usage.data.budgetBrl)} em {formatMonth(usage.data.month)} ·{' '}
                {usage.data.calls === 1 ? '1 chamada' : `${usage.data.calls} chamadas`}
              </p>
              <p className="text-muted-foreground text-sm">
                Ao atingir o limite, a IA fica indisponível até o mês seguinte. Chamadas que falham também
                contam, porque o provedor cobra pelo que recebeu.
              </p>
            </>
          )}
        </section>
      </div>
    </section>
  );
}

function Disclosure() {
  return (
    <div data-testid="ai-disclosure" className="grid gap-3 text-sm leading-relaxed">
      <h3 className="font-medium">O que é enviado ao provedor de IA</h3>
      <p>
        Com a IA ligada, alguns dados saem deste servidor e vão para o provedor de IA escolhido por quem
        administra o app: a Anthropic (Claude) ou a OpenAI (ChatGPT). O envio só acontece quando você pede, em
        "Sugerir com IA" ou em "Gerar análise". Com a IA desligada, nada é enviado.
      </p>

      <h4 className="font-medium">Sugerir com IA, na importação</h4>
      <ul className="list-disc space-y-1 pl-5">
        <li>
          a descrição de cada linha que ficou na categoria padrão, normalizada: em maiúsculas, sem acentos e sem
          números, o que remove CPF, CNPJ, números de cartão e de conta e datas;
        </li>
        <li>se cada uma dessas linhas é um débito ou um crédito;</li>
        <li>os nomes das suas categorias.</li>
      </ul>
      <p className="text-muted-foreground">
        Não são enviados valores, datas nem contas. Nomes que aparecem na descrição, como o de quem recebeu um
        PIX, são enviados.
      </p>

      <h4 className="font-medium">Análise do mês, no painel</h4>
      <ul className="list-disc space-y-1 pl-5">
        <li>os nomes, tipos e saldos das suas contas;</li>
        <li>os nomes das suas categorias, com o total de cada uma nos três últimos meses;</li>
        <li>as receitas, as despesas e o resultado desses três meses;</li>
        <li>
          até 20 destinos dos seus gastos no mês, os de maior valor, pela descrição normalizada, com o total
          gasto e o número de lançamentos. Um PIX enviado a uma pessoa leva o nome dela.
        </li>
        <li>os três totais da sua carteira de investimentos: valor atual, custo e resultado não realizado.</li>
      </ul>
      <p className="text-muted-foreground">
        Não são enviados lançamentos um a um, descrições originais, datas nem descrições de receitas.
      </p>
    </div>
  );
}
