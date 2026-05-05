import { Routes } from '@angular/router';
import { SystemStatus } from './core/system-status/system-status';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'status' },
  { path: 'status', component: SystemStatus },
];
