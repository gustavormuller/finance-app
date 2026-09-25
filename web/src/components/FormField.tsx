import { Label } from '@/components/ui/label';

/**
 * A label, its control, and the API's messages for that field under it. The messages
 * are the problem details' `errors[field]`, pt-BR, shown as sent.
 */
export default function FormField({
  id,
  label,
  errors,
  children,
}: {
  id: string;
  label: string;
  errors?: string[] | undefined;
  children: React.ReactNode;
}) {
  return (
    <div className="grid content-start gap-2">
      <Label htmlFor={id}>{label}</Label>
      {children}
      {errors && errors.length > 0 && (
        <p role="alert" className="text-destructive text-sm">
          {errors.join(' ')}
        </p>
      )}
    </div>
  );
}

/** The class stack shadcn/ui's Input uses, so a native select sits level with one. */
export const selectClasses =
  'border-input bg-card h-10 w-full rounded-xl border px-3 py-1 ' +
  'text-base shadow-xs outline-none md:text-sm focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50';
