import { createContext, use } from 'react';

/**
 * How a node tile takes part in the canvas's keyboard model.
 *
 * The canvas is one tab stop, not five hundred. Focus roves: exactly one tile carries
 * `tabIndex={0}` and the arrow keys move which — the pattern a toolbar and a tree view use, and
 * the only one that keeps a 500-node graph reachable without burying every control after it
 * under half a thousand stops.
 *
 * It travels by context rather than through the node's `data`, deliberately: React Flow keys its
 * store on node identity, so threading the active id through `data` would rebuild all five
 * hundred node objects on every arrow press and churn the store on a keystroke that moved one
 * thing. This way the node objects are built once and only the tiles re-render.
 */
export interface TopologyFocusValue {
  /** The tile the keyboard is on, or `null` before anything has focused the canvas. */
  readonly activeDeviceId: string | null;
  /** Lets the canvas move real DOM focus when the arrow keys move the active tile. */
  readonly register: (deviceId: string, element: HTMLButtonElement | null) => void;
  /** A tile took focus by being clicked or tabbed into. Keeps the roving index in step. */
  readonly focus: (deviceId: string) => void;
  /** Enter, space, or a click: open the device. */
  readonly activate: (deviceId: string) => void;
}

const noop: TopologyFocusValue = {
  activeDeviceId: null,
  register: () => undefined,
  focus: () => undefined,
  activate: () => undefined,
};

export const TopologyFocusContext = createContext<TopologyFocusValue>(noop);

/** What a node tile reads. Defaults to doing nothing, so a tile rendered alone still renders. */
export function useTopologyFocus(): TopologyFocusValue {
  return use(TopologyFocusContext);
}
