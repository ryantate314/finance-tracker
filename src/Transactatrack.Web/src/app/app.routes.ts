import { Routes } from '@angular/router';
import { SystemStatus } from './core/system-status/system-status';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'status' },
  { path: 'status', component: SystemStatus },
  { path: 'families', loadComponent: () => import('./features/families/families-list').then(m => m.FamiliesList) },
  { path: 'owners', loadComponent: () => import('./features/owners/owners-list').then(m => m.OwnersList) },
  { path: 'accounts', loadComponent: () => import('./features/accounts/accounts-list').then(m => m.AccountsList) },
  { path: 'categories', loadComponent: () => import('./features/categories/categories-page').then(m => m.CategoriesPage) },
  { path: 'imports', loadComponent: () => import('./features/imports/imports-page').then(m => m.ImportsPage) },
  { path: 'imports/:id', loadComponent: () => import('./features/imports/import-preview-page').then(m => m.ImportPreviewPage) },
  { path: 'ledger', loadComponent: () => import('./features/ledger/ledger-page').then(m => m.LedgerPage) },
];
