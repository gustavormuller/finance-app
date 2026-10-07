import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { api, ApiError, type Movement, type MovementInput } from '@/api/finance';
import Alert from '@/components/Alert';
import SectionHeading from '@/components/dashboard/SectionHeading';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { formatDate, movementKindLabels } from '@/lib/labels';
import { formatMoney, formatQuantity, formatUnitPrice, localToday } from '@/lib/money';
import { useOpenForm, type Opening } from '@/lib/openForm';

import MovementForm from './MovementForm';
import { INVESTMENTS, useMovements } from './queries';

/**
 * The asset's movements, oldest first as the API replays them, with add, edit and
 * delete. A rule broken on a write comes back as a 400 and is shown under
 * its field; a delete that would uncover a later sell is a 409, shown as sent.
 */
export default function Movements({ assetId, currency }: { assetId: string; currency: string }) {
  const queryClient = useQueryClient();
  const movements = useMovements(assetId);
  // The open form's movement, or null for a new one.
  const forms = useOpenForm<Movement | null>();
  const opening = forms.open;
  const editing = opening?.of ?? null;
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [failure, setFailure] = useState<string | null>(null);

  const open = (movement: Movement | null) => {
    forms.show(movement);
    setErrors({});
    setFailure(null);
  };
  const close = () => {
    forms.close();
    setErrors({});
  };

  const save = useMutation({
    mutationFn: ({ input, from }: { input: MovementInput; from: Opening<Movement | null> }) =>
      from.of ? api.updateMovement(from.of.id, input) : api.createMovement(assetId, input),
    onMutate: () => {
      setErrors({});
      setFailure(null);
    },
    onSuccess: async (_, { from }) => {
      await queryClient.invalidateQueries({ queryKey: INVESTMENTS });
      if (forms.current() === from) {
        close();
      }
    },
    onError: (error: Error) => {
      const fields = error instanceof ApiError ? error.fields : {};
      setErrors(fields);
      setFailure(Object.keys(fields).length === 0 ? error.message : null);
    },
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteMovement(id),
    onMutate: () => setFailure(null),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: INVESTMENTS }),
    onError: (error: Error) => setFailure(error.message),
  });

  const money = (value: number) => formatMoney(value, currency);

  return (
    <section aria-labelledby="movements-heading" className="grid gap-4">
      <div className="flex items-center justify-between gap-4">
        <SectionHeading id="movements-heading">Movimentações</SectionHeading>
        {!opening && <Button onClick={() => open(null)}>Nova movimentação</Button>}
      </div>

      {failure && <Alert>{failure}</Alert>}

      {opening && (
        <div className="glass rounded-2xl p-5 sm:p-6">
          <MovementForm
            key={editing?.id ?? 'new'}
            currency={currency}
            today={localToday()}
            movement={editing ?? undefined}
            errors={errors}
            pending={save.isPending}
            submitLabel={editing ? 'Salvar movimentação' : 'Registrar movimentação'}
            onSubmit={(input) => save.mutate({ input, from: opening })}
            onCancel={close}
          />
        </div>
      )}

      {movements.isError && <Alert>Não foi possível carregar as movimentações.</Alert>}

      {movements.data?.length === 0 ? (
        <p className="text-muted-foreground glass rounded-2xl py-12 text-center text-sm">Nenhuma movimentação registrada.</p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Data</TableHead>
              <TableHead>Tipo</TableHead>
              <TableHead className="text-right">Quantidade</TableHead>
              <TableHead className="text-right">Preço unitário</TableHead>
              <TableHead className="text-right">Valor</TableHead>
              <TableHead className="hidden text-right sm:table-cell">Taxas</TableHead>
              <TableHead className="hidden md:table-cell">Observações</TableHead>
              <TableHead />
            </TableRow>
          </TableHeader>
          <TableBody>
            {(movements.data ?? []).map((movement) => {
              const income = movement.kind === 'Dividend' || movement.kind === 'Jcp';

              return (
                <TableRow key={movement.id} data-testid={`movement-${movement.id}`}>
                  <TableCell className="tabular-nums">{formatDate(movement.date)}</TableCell>
                  <TableCell>{movementKindLabels[movement.kind]}</TableCell>
                  <TableCell className="text-right tabular-nums">{income ? '—' : formatQuantity(movement.quantity)}</TableCell>
                  <TableCell className="text-right tabular-nums">
                    {income || movement.kind === 'Split' ? '—' : formatUnitPrice(movement.unitPrice, currency)}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{income ? money(movement.amount) : '—'}</TableCell>
                  <TableCell className="hidden text-right tabular-nums sm:table-cell">{money(movement.fees)}</TableCell>
                  <TableCell className="hidden whitespace-normal md:table-cell">{movement.notes}</TableCell>
                  <TableCell>
                    <div className="flex justify-end gap-1">
                      <Button variant="ghost" size="sm" onClick={() => open(movement)}>
                        Editar
                      </Button>
                      <Button variant="ghost" size="sm" disabled={remove.isPending} onClick={() => remove.mutate(movement.id)}>
                        Excluir
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      )}
    </section>
  );
}
