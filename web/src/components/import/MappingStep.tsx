import { useEffect, useRef, useState } from 'react';

import type { AmountCulture, CsvMappingInput, CsvPreview, CsvTemplate, SignMode } from '@/api/finance';
import Alert from '@/components/Alert';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  formatPreviewAmount,
  formatPreviewDate,
  interpretRows,
  type MappingDraft,
} from '@/lib/csvPreview';
import { amountCultureLabels, signModeLabels } from '@/lib/labels';

import { selectClasses } from '@/components/FormField';

const DATE_FORMATS = ['dd/MM/yyyy', 'MM/dd/yyyy', 'yyyy-MM-dd', 'd/M/yyyy', 'dd/MM/yy', 'dd/MM/yyyy HH:mm'];

const CULTURES: AmountCulture[] = ['pt-BR', 'en-US'];

const SIGN_MODES: SignMode[] = ['Signed', 'SignedInverted', 'DebitCredit'];

const emptyDraft: MappingDraft = {
  hasHeader: true,
  culture: 'pt-BR',
  dateFormat: 'dd/MM/yyyy',
  signMode: 'Signed',
  dateColumn: '',
  amountColumn: '',
  debitColumn: '',
  creditColumn: '',
  descriptionColumns: [],
};

function fromTemplate(template: CsvTemplate): MappingDraft {
  return {
    hasHeader: template.hasHeader,
    culture: template.culture,
    dateFormat: template.dateFormat,
    signMode: template.signMode,
    dateColumn: template.dateColumn,
    amountColumn: template.amountColumn ?? '',
    debitColumn: template.debitColumn ?? '',
    creditColumn: template.creditColumn ?? '',
    descriptionColumns: template.descriptionColumns
      .split(',')
      .map((part) => part.trim())
      .filter((part) => part !== ''),
  };
}

/**
 * Step 2, CSV or spreadsheet: the real headers and first rows of the file as a table,
 * the controls that say how to read them, and — the feature that makes this usable — a
 * live rendering of those rows as they would be interpreted. Choosing `dd/MM/yyyy`
 * against `MM/dd/yyyy` is invisible until `03/04` shows as 3 abr rather than 4 mar.
 *
 * A spreadsheet comes with `delimiter` null and `onFormatChange`: its typed
 * cells are written by the API in the chosen culture and date format, so a change
 * asks for the preview again instead of only re-rendering it.
 */
export default function MappingStep({
  preview,
  templates,
  delimiter,
  busy,
  error,
  onDelimiterChange,
  onFormatChange,
  onBack,
  onSubmit,
}: {
  preview: CsvPreview;
  templates: CsvTemplate[];
  delimiter: string | null;
  busy: boolean;
  error: React.ReactNode;
  onDelimiterChange: (delimiter: string) => void;
  onFormatChange?: ((culture: AmountCulture, dateFormat: string) => void) | undefined;
  onBack: () => void;
  onSubmit: (mapping: CsvMappingInput, saveAsTemplate: string | null) => void;
}): React.JSX.Element {
  const [draft, setDraft] = useState<MappingDraft>(emptyDraft);
  const [templateId, setTemplateId] = useState('');
  const [saveAs, setSaveAs] = useState('');

  const headers = draft.hasHeader ? preview.headers : null;
  const rows = draft.hasHeader ? preview.sampleRows : [preview.headers, ...preview.sampleRows];
  const width = preview.headers.length;

  // What a column is called in the dropdowns, and what its reference is on the
  // wire: the header text when there is one, the 0-based index otherwise.
  const columns = Array.from({ length: width }, (_, index) => ({
    value: headers ? headers[index]! : String(index),
    label: headers ? headers[index]! : `Coluna ${index + 1}`,
  }));

  const interpreted = interpretRows(headers, rows, draft);
  const debitCredit = draft.signMode === 'DebitCredit';

  const update = (change: Partial<MappingDraft>) => setDraft((current) => ({ ...current, ...change }));

  // The first preview was rendered in the defaults; only a change is worth a request,
  // and only once the typing stops and the format can name a day, month and year.
  const rendered = useRef({ culture: emptyDraft.culture, dateFormat: emptyDraft.dateFormat });
  useEffect(() => {
    const dateFormat = draft.dateFormat.trim();
    const unchanged = rendered.current.culture === draft.culture && rendered.current.dateFormat === dateFormat;

    if (!onFormatChange || unchanged || !/d/.test(dateFormat) || !/M/.test(dateFormat) || !/y/.test(dateFormat)) {
      return;
    }

    const timer = setTimeout(() => {
      rendered.current = { culture: draft.culture, dateFormat };
      onFormatChange(draft.culture, dateFormat);
    }, 400);

    return () => clearTimeout(timer);
  }, [draft.culture, draft.dateFormat, onFormatChange]);

  const toggleDescription = (value: string) =>
    setDraft((current) => ({
      ...current,
      descriptionColumns: current.descriptionColumns.includes(value)
        ? current.descriptionColumns.filter((chosen) => chosen !== value)
        : [...current.descriptionColumns, value],
    }));

  const ready =
    draft.dateColumn !== '' &&
    draft.dateFormat.trim() !== '' &&
    draft.descriptionColumns.length > 0 &&
    (debitCredit ? draft.debitColumn !== '' && draft.creditColumn !== '' : draft.amountColumn !== '');

  const submit = (event: React.FormEvent) => {
    event.preventDefault();

    onSubmit(
      {
        delimiter: delimiter ?? ';',
        hasHeader: draft.hasHeader,
        culture: draft.culture,
        dateFormat: draft.dateFormat.trim(),
        signMode: draft.signMode,
        dateColumn: draft.dateColumn,
        amountColumn: debitCredit ? null : draft.amountColumn,
        debitColumn: debitCredit ? draft.debitColumn : null,
        creditColumn: debitCredit ? draft.creditColumn : null,
        descriptionColumns: draft.descriptionColumns.join(', '),
      },
      saveAs.trim() === '' ? null : saveAs.trim(),
    );
  };

  return (
    <form noValidate onSubmit={submit} className="grid gap-8">
      <section>
        <h3 className="mb-2 text-base font-semibold">Como o arquivo chegou</h3>
        {preview.skippedRows > 0 && (
          <p className="text-muted-foreground mb-2 text-sm">
            {preview.skippedRows} {preview.skippedRows === 1 ? 'linha ignorada' : 'linhas ignoradas'} acima
            da tabela.
          </p>
        )}
        <div className="overflow-x-auto">
          <Table bare>
            <TableHeader>
              <TableRow>
                {columns.map((column) => (
                  <TableHead key={column.value}>{column.label}</TableHead>
                ))}
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((row, rowIndex) => (
                <TableRow key={rowIndex}>
                  {columns.map((column, columnIndex) => (
                    <TableCell key={column.value} className="whitespace-nowrap">
                      {row[columnIndex] ?? ''}
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </section>

      <section className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {templates.length > 0 && (
          <Field id="mapping-template" label="Modelo salvo">
            <select
              id="mapping-template"
              className={selectClasses}
              value={templateId}
              onChange={(event) => {
                const template = templates.find((candidate) => candidate.id === event.target.value);
                setTemplateId(event.target.value);

                if (template) {
                  setDraft(fromTemplate(template));

                  if (delimiter !== null && template.delimiter !== delimiter) {
                    onDelimiterChange(template.delimiter);
                  }
                }
              }}
            >
              <option value="">Mapear manualmente</option>
              {templates.map((template) => (
                <option key={template.id} value={template.id}>
                  {template.name}
                </option>
              ))}
            </select>
          </Field>
        )}

        {delimiter !== null && (
          <Field id="mapping-delimiter" label="Delimitador">
            <Input
              id="mapping-delimiter"
              value={delimiter === '\t' ? 'tab' : delimiter}
              maxLength={3}
              onChange={(event) => {
                const typed = event.target.value;

                if (typed.toLowerCase() === 'tab') {
                  onDelimiterChange('\t');
                } else if (typed.length === 1) {
                  onDelimiterChange(typed);
                }
              }}
            />
          </Field>
        )}

        <div className="flex items-end gap-2 pb-2">
          <input
            id="mapping-has-header"
            type="checkbox"
            checked={draft.hasHeader}
            onChange={(event) => update({ hasHeader: event.target.checked, dateColumn: '', amountColumn: '', debitColumn: '', creditColumn: '', descriptionColumns: [] })}
          />
          <Label htmlFor="mapping-has-header">Primeira linha é cabeçalho</Label>
        </div>

        <Field id="mapping-culture" label="Formato dos números">
          <select
            id="mapping-culture"
            className={selectClasses}
            value={draft.culture}
            onChange={(event) => update({ culture: event.target.value as AmountCulture })}
          >
            {CULTURES.map((culture) => (
              <option key={culture} value={culture}>
                {amountCultureLabels[culture]}
              </option>
            ))}
          </select>
        </Field>

        <Field id="mapping-date-format" label="Formato da data">
          <Input
            id="mapping-date-format"
            list="mapping-date-formats"
            value={draft.dateFormat}
            onChange={(event) => update({ dateFormat: event.target.value })}
          />
          <datalist id="mapping-date-formats">
            {DATE_FORMATS.map((format) => (
              <option key={format} value={format} />
            ))}
          </datalist>
          {delimiter === null && (
            <p className="text-muted-foreground text-sm">
              Células de data e número da planilha são convertidas para o formato escolhido;
              células de texto precisam estar nesse formato.
            </p>
          )}
        </Field>

        <Field id="mapping-sign-mode" label="Sinal">
          <select
            id="mapping-sign-mode"
            className={selectClasses}
            value={draft.signMode}
            onChange={(event) => update({ signMode: event.target.value as SignMode })}
          >
            {SIGN_MODES.map((mode) => (
              <option key={mode} value={mode}>
                {signModeLabels[mode]}
              </option>
            ))}
          </select>
        </Field>

        <ColumnField
          id="mapping-date-column"
          label="Coluna de data"
          value={draft.dateColumn}
          columns={columns}
          onChange={(dateColumn) => update({ dateColumn })}
        />

        {debitCredit ? (
          <>
            <ColumnField
              id="mapping-debit-column"
              label="Coluna de débito"
              value={draft.debitColumn}
              columns={columns}
              onChange={(debitColumn) => update({ debitColumn })}
            />
            <ColumnField
              id="mapping-credit-column"
              label="Coluna de crédito"
              value={draft.creditColumn}
              columns={columns}
              onChange={(creditColumn) => update({ creditColumn })}
            />
          </>
        ) : (
          <ColumnField
            id="mapping-amount-column"
            label="Coluna de valor"
            value={draft.amountColumn}
            columns={columns}
            onChange={(amountColumn) => update({ amountColumn })}
          />
        )}

        <fieldset className="grid gap-2 sm:col-span-2 lg:col-span-3">
          <legend className="text-sm font-medium">Colunas de descrição, na ordem escolhida</legend>
          <div className="flex flex-wrap gap-3">
            {columns.map((column) => {
              const position = draft.descriptionColumns.indexOf(column.value);

              return (
                <label key={column.value} className="inline-flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={position >= 0}
                    onChange={() => toggleDescription(column.value)}
                  />
                  {column.label}
                  {position >= 0 && (
                    <span className="bg-secondary text-secondary-foreground rounded px-1.5 text-xs">
                      {position + 1}º
                    </span>
                  )}
                </label>
              );
            })}
          </div>
        </fieldset>
      </section>

      <section>
        <h3 className="mb-2 text-base font-semibold">Como será lido</h3>
        <div className="overflow-x-auto">
          <Table bare>
            <TableHeader>
              <TableRow>
                <TableHead>Data</TableHead>
                <TableHead>Descrição</TableHead>
                <TableHead className="text-right">Valor</TableHead>
                <TableHead>Problemas</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody data-testid="mapping-preview">
              {interpreted.map((row, index) => (
                <TableRow key={index}>
                  <TableCell className="whitespace-nowrap tabular-nums">
                    {row.date ? formatPreviewDate(row.date) : '—'}
                  </TableCell>
                  <TableCell>{row.description || '—'}</TableCell>
                  <TableCell className="text-right tabular-nums">
                    {row.amount ? formatPreviewAmount(row.amount) : '—'}
                  </TableCell>
                  <TableCell className="text-destructive text-sm">{row.issues.join(' · ')}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </section>

      <section className="grid max-w-md gap-2">
        <Label htmlFor="mapping-save-as">Salvar como modelo (opcional)</Label>
        <Input
          id="mapping-save-as"
          placeholder="Ex.: Nubank conta"
          value={saveAs}
          onChange={(event) => setSaveAs(event.target.value)}
        />
      </section>

      {error && <Alert>{error}</Alert>}

      <div className="flex gap-2">
        <Button type="submit" disabled={!ready || busy}>
          {busy ? 'Enviando…' : 'Continuar'}
        </Button>
        <Button type="button" variant="outline" onClick={onBack}>
          Voltar
        </Button>
      </div>
    </form>
  );
}

function Field({ id, label, children }: { id: string; label: string; children: React.ReactNode }) {
  return (
    <div className="grid gap-2">
      <Label htmlFor={id}>{label}</Label>
      {children}
    </div>
  );
}

function ColumnField({
  id,
  label,
  value,
  columns,
  onChange,
}: {
  id: string;
  label: string;
  value: string;
  columns: { value: string; label: string }[];
  onChange: (value: string) => void;
}) {
  return (
    <Field id={id} label={label}>
      <select id={id} className={selectClasses} value={value} onChange={(event) => onChange(event.target.value)}>
        <option value="">Escolha a coluna</option>
        {columns.map((column) => (
          <option key={column.value} value={column.value}>
            {column.label}
          </option>
        ))}
      </select>
    </Field>
  );
}
