import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ActivityMetadata } from '../models/activity.model';

/**
 * Reads the activity catalog exposed by the backend (GET /api/activities).
 * Consumed by the toolbox and the canvas when materialising nodes.
 */
@Injectable({ providedIn: 'root' })
export class ActivityCatalogService {
  private readonly http = inject(HttpClient);

  getActivities(): Observable<ActivityMetadata[]> {
    return this.http.get<ActivityMetadata[]>('/api/activities');
  }

  getActivity(activityId: string): Observable<ActivityMetadata> {
    return this.http.get<ActivityMetadata>(`/api/activities/${encodeURIComponent(activityId)}`);
  }
}
