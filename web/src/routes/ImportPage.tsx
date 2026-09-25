import { Navigate } from '@tanstack/react-router';

import { useImports } from '@/components/accounts/queries';

/**
 * `/import` (015): no longer a page — the import lives in each account — but links and
 * bookmarks to it keep working. With a statement in review, to that account's import
 * tab (the API keeps one per user); otherwise to the accounts.
 */
export default function ImportPage(): React.JSX.Element | null {
  const imports = useImports();

  if (imports.isPending) {
    return null;
  }

  const open = imports.data?.find((batch) => batch.status === 'Staged');

  return open ? (
    <Navigate to="/accounts/$accountId" params={{ accountId: open.accountId }} search={{ tab: 'import' }} replace />
  ) : (
    <Navigate to="/accounts" replace />
  );
}
