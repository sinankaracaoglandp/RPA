import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslatePipe } from '../core/translate.pipe';
import { TranslationService, SupportedLang } from '../core/translation.service';
import { AuthService } from './auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslatePipe],
  templateUrl: './login.component.html',
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  readonly translationService = inject(TranslationService);

  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    username: ['', [Validators.required]],
    password: ['', [Validators.required]],
  });

  changeLanguage(lang: SupportedLang): void {
    void this.translationService.use(lang);
  }

  submit(): void {
    this.errorMessage.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { username, password } = this.form.getRawValue();
    this.submitting.set(true);

    this.authService.login(username, password).subscribe({
      
      next: () => {
        
        this.submitting.set(false);
        void this.router.navigateByUrl('/');
      },
      error: (err) => {
        this.submitting.set(false);
        if (err?.status === 401) {
          this.errorMessage.set(this.translationService.translate('login.errorInvalidCredentials'));
        } else {
          this.errorMessage.set(this.translationService.translate('login.errorGeneric'));
        }
      },
    });
  }
}
