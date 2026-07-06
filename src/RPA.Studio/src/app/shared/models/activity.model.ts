/**
 * Activity metadata as returned by the catalog endpoint (GET /api/activities).
 * Mirrors RPA.Domain ActivityMetadata (Spec Bölüm 2 — activity catalog).
 */
export interface ActivityPort {
  name: string;
  type: string;
  required?: boolean;
  description?: string;
}

export interface ActivityMetadata {
  activityId: string;
  displayName: string;
  category?: string;
  description?: string;
  icon?: string;
  inputs?: ActivityPort[];
  outputs?: ActivityPort[];
  /** Katalogda tanımlı başlangıç özellik değerleri (node oluşturmada kopyalanır). */
  defaultProperties?: Record<string, unknown>;
}
