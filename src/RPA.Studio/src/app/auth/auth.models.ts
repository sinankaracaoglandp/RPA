export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  refreshToken: string;
  accessTokenExpiresAtUtc: string;
  roles: string[];
}

export interface RefreshRequest {
  refreshToken: string;
}
