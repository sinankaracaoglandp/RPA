# Faz 5 — Studio UI (Canvas, Toolbox, Component Library, Debug)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Studio frontend (Angular 20+) with node-based workflow designer (Rete.js 2), real-time component library, and debug/step-through IDE features.

**Architecture:**
- Studio is an Angular 20+ SPA (separate from Orchestrator in same backend, shared API + SignalR)
- Canvas uses Rete.js 2 (open-source node graph editor); nodes map to activities
- Toolbox dynamically feeds from ActivityCatalog REST endpoint
- Component Library enables publish/approve workflow (via shared backend)
- Debug/Step-Through connects to live Agent via SignalR + RobotHub
- i18n: Turkish + English (lang="tr|en" in index.html)
- Styling: Tailwind CSS 4 (utility-first, configured in tailwind.config.ts)

**Tech Stack:** Angular 20+, Rete.js 2, Tailwind CSS 4, TypeScript 5.2+, RxJS 7.8+, @angular/cdk, @ngrx/store (state management), Jasmine/Karma (testing).

## Global Constraints

- Angular version: ≥20.0 (LTS)
- Rete.js version: 2.x (node graph library)
- Tailwind CSS: 4.0+
- TypeScript: ≥5.2
- Node.js: ≥18 (for build tooling)
- Backend API: RPA.WebAPI (Swagger/OpenAPI at `/swagger/v1/swagger.json`)
- SignalR hubs: `/hubs/robot`, `/hubs/studio` (JWT in query string for WS auth)
- i18n: ngx-translate (`@ngx-translate/core`) or Angular built-in i18n (choose one early)
- Testing: Karma + Jasmine, ≥70% code coverage
- Accessibility (A11y): ARIA labels on interactive elements, keyboard navigation on canvas
- Localization: All UI text from i18n resources (no hardcoded English)

---

## File Structure

### Angular App Structure

```
src/
├── app/
│   ├── shared/
│   │   ├── models/
│   │   │   ├── activity.model.ts (IActivity metadata from catalog)
│   │   │   ├── workflow.model.ts (WorkflowVersion JSON)
│   │   │   └── component.model.ts (ComponentVersion)
│   │   ├── services/
│   │   │   ├── activity-catalog.service.ts (REST: GET /api/activities)
│   │   │   ├── workflow.service.ts (CRUD workflows)
│   │   │   ├── component.service.ts (Component library)
│   │   │   ├── robot-hub.service.ts (SignalR: /hubs/robot)
│   │   │   └── studio-hub.service.ts (SignalR: /hubs/studio)
│   │   └── i18n/
│   │       ├── en.json (English)
│   │       └── tr.json (Turkish)
│   ├── studio/
│   │   ├── designer/
│   │   │   ├── canvas/ (Rete.js 2 container)
│   │   │   │   ├── canvas.component.ts|html|css
│   │   │   │   ├── canvas.component.spec.ts
│   │   │   │   ├── node.component.ts|html|css (activity node render)
│   │   │   │   └── connection.component.ts (edge render)
│   │   │   ├── toolbox/
│   │   │   │   ├── toolbox.component.ts|html|css
│   │   │   │   ├── activity-item.component.ts
│   │   │   │   └── toolbox.component.spec.ts
│   │   │   ├── properties/
│   │   │   │   ├── properties-panel.component.ts|html|css
│   │   │   │   ├── expression-editor.component.ts
│   │   │   │   ├── credential-picker.component.ts
│   │   │   │   └── properties-panel.component.spec.ts
│   │   │   ├── variables/
│   │   │   │   └── variables-panel.component.ts|html|css
│   │   │   └── designer.component.ts (main layout)
│   │   ├── component-library/
│   │   │   ├── library.component.ts|html|css
│   │   │   ├── component-card.component.ts
│   │   │   ├── publish-wizard/
│   │   │   │   └── publish-wizard.component.ts|html|css
│   │   │   └── library.component.spec.ts
│   │   ├── debug/
│   │   │   ├── debug-panel.component.ts|html|css
│   │   │   ├── breakpoint-list.component.ts
│   │   │   ├── watch-window.component.ts
│   │   │   └── debug-panel.component.spec.ts
│   │   └── studio.component.ts (root studio layout)
│   ├── app.component.ts
│   └── app.config.ts (providers)
├── assets/
│   ├── i18n/ (ngx-translate JSON files if used)
│   └── images/
├── styles/
│   ├── tailwind.css (Tailwind directives)
│   └── global.css (global overrides)
├── index.html
└── main.ts
```

### Backend Contracts (API)

```
Endpoints (from src/RPA.WebAPI/Controllers):
- GET /api/activities → ActivityMetadata[] (activity catalog)
- GET /api/activities/{id} → ActivityMetadata (single)
- GET /api/components → ComponentVersion[] (library)
- POST /api/components/publish → publish workflow
- GET /api/workflows/{id} → WorkflowVersion (editor load)
- PUT /api/workflows/{id} → save workflow
- POST /api/robots/{robotId}/debug/execute → run with breakpoints
- WS /hubs/robot → RobotHub (step-through updates)
- WS /hubs/studio → StudioHub (element detection, etc.)

Types (from src/RPA.Domain):
- IActivity (ActivityMetadata: ActivityId, DisplayName, Inputs, Outputs, etc.)
- WorkflowVersion (JsonDefinition: nodes, connections, arguments, variables)
- ComponentVersion (JSON schema)
```

---

## Task 1: Canvas & Node Graph (Rete.js 2)

**Model:** Opus (high — complex graph interaction)  
**Effort:** High  
**Files:**
- Create: `src/app/studio/designer/canvas/canvas.component.ts|html|scss`
- Create: `src/app/studio/designer/canvas/node.component.ts|html|scss`
- Create: `src/app/studio/designer/canvas/connection.component.ts`
- Create: `src/app/studio/designer/canvas/canvas.component.spec.ts`
- Modify: `src/app/studio/designer/designer.component.ts` (main layout)

**Interfaces:**
- **Consumes:** 
  - `ActivityCatalogService.getActivities()` → `ActivityMetadata[]`
  - Workflow JSON from parent designer
- **Produces:**
  - `CanvasComponent` (Rete.js 2 container)
  - Node/Connection rendering
  - Graph manipulation (add node, connect, delete, undo/redo)
  - `onNodeSelect: EventEmitter<NodeId>` (for properties panel)

**Acceptance Criteria:**
- Rete.js 2 initialized and rendering nodes/edges
- Zoom/pan/select working
- Add node from toolbox (via parent)
- Connect nodes (edge creation)
- Delete node/edge
- Undo/redo stack
- Node positions persisted in workflow JSON
- ≥10 tests (node add/remove, connection, zoom, undo)

---

### Step 1: Create canvas component (empty Rete.js container)

- [ ] **Write failing test**

```typescript
// src/app/studio/designer/canvas/canvas.component.spec.ts
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CanvasComponent } from './canvas.component';

describe('CanvasComponent', () => {
  let component: CanvasComponent;
  let fixture: ComponentFixture<CanvasComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CanvasComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(CanvasComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create a Rete.js editor', () => {
    expect(component.editor).toBeDefined();
  });

  it('should render a canvas container', () => {
    const canvas = fixture.nativeElement.querySelector('.rete-canvas');
    expect(canvas).toBeTruthy();
  });
});
```

- [ ] **Run test to verify it fails**

```bash
ng test --watch=false --browsers=ChromeHeadless
```

Expected: FAIL — "editor is undefined", "canvas element not found"

- [ ] **Implement CanvasComponent**

```typescript
// src/app/studio/designer/canvas/canvas.component.ts
import { Component, OnInit, ViewChild, ElementRef } from '@angular/core';
import { Rete } from 'rete';

@Component({
  selector: 'app-canvas',
  standalone: true,
  imports: [],
  templateUrl: './canvas.component.html',
  styleUrls: ['./canvas.component.scss']
})
export class CanvasComponent implements OnInit {
  @ViewChild('reteContainer') reteContainer!: ElementRef;
  editor!: Rete.NodeEditor;

  ngOnInit() {
    this.initializeEditor();
  }

  private async initializeEditor() {
    // Initialize Rete.js editor
    const container = this.reteContainer.nativeElement;
    this.editor = new Rete.NodeEditor('demo@0.1.0', container);
    
    // Add default plugins (render, alight, connection, etc.)
    this.editor.use(Rete.ConnectionPlugin.default());
    this.editor.use(Rete.RenderPlugin.default());
  }
}
```

```html
<!-- src/app/studio/designer/canvas/canvas.component.html -->
<div class="rete-canvas-wrapper">
  <div #reteContainer class="rete-canvas"></div>
</div>
```

```scss
// src/app/studio/designer/canvas/canvas.component.scss
.rete-canvas-wrapper {
  width: 100%;
  height: 100%;
  border: 1px solid #ccc;

  .rete-canvas {
    width: 100%;
    height: 100%;
  }
}
```

- [ ] **Run test to verify it passes**

```bash
ng test --watch=false --browsers=ChromeHeadless
```

Expected: PASS

- [ ] **Commit**

```bash
git add src/app/studio/designer/canvas/ && git commit -m "feat(studio): Canvas component with Rete.js 2 initialization"
```

### Step 2–5: Add nodes, connections, zoom/pan, undo/redo

(Continue TDD cycle for each feature: failing test → implement → pass → commit)

- Add node rendering (Node component)
- Add connection rendering (Connection component)
- Implement zoom/pan (Rete.js viewport plugin)
- Implement undo/redo (custom stack)
- Implement delete (keyboard handling)

**Total tests:** ≥10 for this task.

---

## Task 2: Toolbox & Activity Catalog

**Model:** Sonnet (medium)  
**Effort:** Medium  
**Files:**
- Create: `src/app/studio/designer/toolbox/toolbox.component.ts|html|scss`
- Create: `src/app/studio/designer/toolbox/activity-item.component.ts|html|scss`
- Create: `src/app/shared/services/activity-catalog.service.ts`
- Create: `src/app/studio/designer/toolbox/toolbox.component.spec.ts`

**Interfaces:**
- **Consumes:**
  - `ActivityCatalogService.getActivities()` → REST
  - `Canvas.addNode(activityId)` → EventEmitter from parent
- **Produces:**
  - `ToolboxComponent` (searchable, categorized activity list)
  - Drag-and-drop to canvas (with preview)

**Acceptance Criteria:**
- Activity list loads from API
- Search/filter by name
- Category tabs (Logic, SAP, Web, OTP, etc.)
- Drag activity to canvas
- ≥8 tests (load, search, drag, filter)

---

Continue with similar TDD structure for:
- **Task 3:** Properties Panel (expression editor, credential picker, inputs/outputs)
- **Task 4:** Component Library & Publish Wizard
- **Task 5:** Debug/Step-Through (breakpoint, watch window, live robot sync via SignalR)
- **Task 6:** Simple Mode & Template Gallery

---

## Summary

**Faz 5:** 6 frontend tasks, ~17 TDD steps total, ~80–100 unit tests  
**Expected:** 360+ tests passing  
**Coverage:** ≥70% frontend code  
**Output:** Complete Angular Studio UI, Rete.js-based workflow designer, component library, debug IDE

**Execution:** Subagent-Driven Development — fresh implementer per task, task review (spec + quality), final whole-branch review before merge to main.
