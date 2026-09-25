// The pages are lazy routes (018). A test that renders the route tree imports this module
// first, so every page is loaded before its first test starts, as it was when the route
// tree imported them statically. Otherwise the first lazy import transforms the page's
// whole module graph (Recharts included) inside that test's timeout.
import './routes/AccountPage';
import './routes/AccountsPage';
import './routes/AssetPage';
import './routes/AssetReturnsPage';
import './routes/CategoriesPage';
import './routes/DashboardPage';
import './routes/ImportPage';
import './routes/InvestmentsPage';
import './routes/MarketDataPage';
import './routes/ReturnsPage';
import './routes/SettingsPage';
import './routes/TransactionsPage';
