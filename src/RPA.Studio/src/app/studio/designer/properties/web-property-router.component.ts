import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { WorkflowVariable } from '../../../shared/models/workflow.model';
import { WebClickPropertyComponent } from './web/web-click-property.component';
import { WebGetTextPropertyComponent } from './web/web-get-text-property.component';
import { WebNavigatePropertyComponent } from './web/web-navigate-property.component';
import { WebSetTextPropertyComponent } from './web/web-set-text-property.component';
import { WebWaitForSelectorPropertyComponent } from './web/web-wait-for-selector-property.component';

export const WEB_ACTIVITY_TYPES = [
  'Web.Goto',
  'Web.Click',
  'Web.Fill',
  'Web.GetText',
  'Web.WaitFor',
  'Web.Navigate',
  'Web.SetText',
  'Web.WaitForSelector',
] as const;

export type WebActivityType = (typeof WEB_ACTIVITY_TYPES)[number];

/** Returns true if the given activity id is one of the Web.* activities handled here. */
export function isWebActivityType(activityType: string | null | undefined): activityType is WebActivityType {
  return !!activityType && (WEB_ACTIVITY_TYPES as readonly string[]).includes(activityType);
}

/**
 * Routes the properties panel to the correct Web.* activity editor based on
 * the selected node's activity type (Faz 5 Task 5.6). Renders nothing for
 * unknown/non-web activity types so it can be composed alongside other
 * activity-family routers in the properties panel.
 */
@Component({
  selector: 'app-web-property-router',
  standalone: true,
  imports: [
    CommonModule,
    WebNavigatePropertyComponent,
    WebClickPropertyComponent,
    WebSetTextPropertyComponent,
    WebGetTextPropertyComponent,
    WebWaitForSelectorPropertyComponent,
  ],
  templateUrl: './web-property-router.component.html',
})
export class WebPropertyRouterComponent {
  @Input() activityType: string | null | undefined;
  @Input() properties: Record<string, unknown> | null | undefined;
  @Input() variables: WorkflowVariable[] = [];
  @Output() readonly propertiesChange = new EventEmitter<Record<string, unknown>>();

  get isNavigate(): boolean {
    return this.activityType === 'Web.Goto' || this.activityType === 'Web.Navigate';
  }

  get isClick(): boolean {
    return this.activityType === 'Web.Click';
  }

  get isSetText(): boolean {
    return this.activityType === 'Web.Fill' || this.activityType === 'Web.SetText';
  }

  get isGetText(): boolean {
    return this.activityType === 'Web.GetText';
  }

  get isWaitForSelector(): boolean {
    return this.activityType === 'Web.WaitFor' || this.activityType === 'Web.WaitForSelector';
  }

  onPropertiesChange(value: Record<string, unknown>): void {
    this.propertiesChange.emit(value);
  }
}
