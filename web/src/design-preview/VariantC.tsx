import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

import { byDateDescending, formatAmount, formatDay } from './fixture';

/**
 * C — Control.
 *
 * Stock shadcn/ui with the stock neutral theme and no decisions of mine on top. The
 * point of this one is to be un-designed: it shows what the defaults give you, so the
 * other five can be judged against something rather than against each other.
 *
 * The only two departures are the ones the brief makes unconditional — tabular
 * figures and right-aligned amounts — because without them no variant is comparable.
 */
export default function VariantC() {
  return (
    <div className="min-h-screen bg-background p-8 text-foreground">
      <div className="mx-auto max-w-5xl">
        <div className="mb-6 flex items-center justify-between gap-4">
          <h1 className="text-2xl font-semibold tracking-tight">Transactions</h1>
          <Button>New transaction</Button>
        </div>

        <div className="mb-6 flex flex-wrap gap-2">
          <Input className="max-w-[200px]" placeholder="From" defaultValue="2026-08-25" />
          <Input className="max-w-[200px]" placeholder="To" defaultValue="2026-09-14" />
        </div>

        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-[80px]">Date</TableHead>
              <TableHead>Description</TableHead>
              <TableHead>Category</TableHead>
              <TableHead>Account</TableHead>
              <TableHead className="text-right">Amount</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {byDateDescending.map((row) => (
              <TableRow key={row.id}>
                <TableCell className="tabular-nums">{formatDay(row.date)}</TableCell>
                <TableCell className="font-medium">{row.description}</TableCell>
                <TableCell>{row.category}</TableCell>
                <TableCell>{row.account}</TableCell>
                <TableCell
                  className={`text-right tabular-nums ${
                    row.amount < 0 ? '' : 'text-green-600'
                  }`}
                >
                  {formatAmount(row.amount)}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}
