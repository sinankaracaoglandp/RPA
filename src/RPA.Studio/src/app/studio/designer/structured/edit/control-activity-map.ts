import { ContainerType } from '../structured-model';

/**
 * Konteyner tipini özellik panelinin beklediği aktivite kimliğine eşler
 * (canvas'taki NODE_TYPE_TO_CONTROL_ACTIVITY ile aynı değerler; canvas iç sabiti export değil).
 */
export const CONTROL_ACTIVITY_OF: Record<ContainerType, string> = {
  if: 'Logic.If',
  forEach: 'Logic.ForEach',
  for: 'Logic.For',
  while: 'Logic.While',
  tryCatch: 'Logic.TryCatch',
};

/** Ters eşleme: kontrol-akışı aktivite kimliği → konteyner tipi (ör. 'Logic.ForEach' → 'forEach'). */
export const CONTAINER_OF_ACTIVITY: Record<string, ContainerType> = Object.fromEntries(
  Object.entries(CONTROL_ACTIVITY_OF).map(([type, activityId]) => [activityId, type as ContainerType]),
) as Record<string, ContainerType>;

/** Kontrol-akışı aktivite kimlikleri kümesi (düz aktivite listelerinden çıkarmak için). */
export const CONTROL_ACTIVITY_IDS: ReadonlySet<string> = new Set(Object.values(CONTROL_ACTIVITY_OF));
