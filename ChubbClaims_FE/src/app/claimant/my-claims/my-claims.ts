import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Placeholder: built in web phase 3. */
@Component({
  selector: 'app-my-claims',
  template: '<h1>My claims</h1><p>Built in web phase 3.</p>',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyClaims {}
