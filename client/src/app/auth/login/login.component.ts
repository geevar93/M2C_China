import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { extractErrorMessage } from '../../core/services/problem-details.util';

/**
 * Login screen — no prototype precedent (TECH_SPEC OI-4/ACTION_PLAN E0-02).
 * Built from docs/DESIGN_TOKENS.md atoms only (card, field, primary button);
 * still needs business sign-off (E0-06) — flagged in the coordinator report.
 */
@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss'
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly loading = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]]
  });

  submit(): void {
    if (this.form.invalid || this.loading()) {
      this.form.markAllAsTouched();
      return;
    }
    this.errorMessage.set(null);
    this.loading.set(true);

    const { email, password } = this.form.getRawValue();
    this.auth.login({ email, password }).subscribe({
      next: (res) => {
        this.loading.set(false);
        this.router.navigate([res.mustChangePassword ? '/force-change-password' : '/dashboard']);
      },
      error: (err) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err, 'Incorrect email or password.'));
      }
    });
  }
}
