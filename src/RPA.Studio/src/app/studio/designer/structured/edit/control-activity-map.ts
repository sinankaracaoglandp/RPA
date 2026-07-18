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
