import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * Her giden isteğe token varsa Authorization: Bearer <token> header'ı ekler.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const token = authService.getToken();
  const isAuthEndpoint = req.url.includes('/api/auth/login') || req.url.includes('/api/auth/refresh');

  const authorizedReq = token && !isAuthEndpoint ? req.clone({
    setHeaders: { Authorization: `Bearer ${token}` },
  }) : req;

  return next(authorizedReq).pipe(
    catchError((error) => {
      if (isAuthEndpoint || error?.status !== 401 || !authService.getRefreshToken()) {
        return throwError(() => error);
      }

      return authService.refreshToken().pipe(
        switchMap((response) =>
          next(req.clone({
            setHeaders: { Authorization: `Bearer ${response.token}` },
          })),
        ),
        catchError((refreshError) => {
          authService.logout();
          return throwError(() => refreshError);
        }),
      );
    }),
  );
};
