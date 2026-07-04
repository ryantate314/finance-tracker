import { Component, input, output } from '@angular/core';
import { AccountDto } from './accounts.service';

@Component({
  selector: 'app-account-picker',
  standalone: true,
  template: `
    <select class="picker-select" (change)="onChange($event)">
      @for (a of accounts(); track a.id) {
        <option [value]="a.id" [selected]="a.id === accountId()">{{ a.name }}</option>
      }
    </select>
  `,
  styles: [`
    :host { display: inline-block; width: 100%; }
    .picker-select {
      width: 100%;
      box-sizing: border-box;
      padding: 6px 8px;
      font: inherit;
      font-size: 0.875rem;
      color: inherit;
      background: transparent;
      border: 1px solid transparent;
      border-radius: 4px;
      cursor: pointer;
      outline: none;
      appearance: none;
      -webkit-appearance: none;
      transition: border-color 0.15s ease, background 0.15s ease;
    }
    .picker-select:hover { border-color: rgba(0,0,0,0.18); background: rgba(0,0,0,0.02); }
    .picker-select:focus { border-color: rgba(0,0,0,0.4); background: rgba(0,0,0,0.02); }
  `],
})
export class AccountPicker {
  accounts = input.required<AccountDto[]>();
  accountId = input<string | null>(null);
  selectionChange = output<string>();

  onChange(e: Event) {
    const id = (e.target as HTMLSelectElement).value;
    this.selectionChange.emit(id);
  }
}
