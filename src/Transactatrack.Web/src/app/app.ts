import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { MatToolbarModule } from '@angular/material/toolbar';
import { FamiliesService } from './features/families/families.service';
import { FamilyContextService } from './core/family-context/family-context.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, MatSelectModule, MatIconModule, MatMenuModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit {
  private familiesSvc = inject(FamiliesService);
  private familyCtx = inject(FamilyContextService);
  private destroyRef = inject(DestroyRef);

  families = toSignal(this.familiesSvc.families$, { initialValue: [] });
  activeFamilyId = this.familyCtx.activeFamilyId;

  ngOnInit() {
    this.familiesSvc.families$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(families => {
        const stored = this.familyCtx.activeFamilyId();
        if (!stored || !families.find(f => f.id === stored)) {
          const defaultFamily = families.find(f => f.id === '00000000-0000-0000-0000-000000000001') ?? families[0];
          if (defaultFamily) this.familyCtx.setActive(defaultFamily.id);
        }
      });
  }

  onFamilyChange(id: string) {
    this.familyCtx.setActive(id);
  }
}
