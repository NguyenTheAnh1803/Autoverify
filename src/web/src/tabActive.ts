import { createContext, useContext } from 'react'

/**
 * Tabs stay mounted (hidden) after they were opened, so state and scroll survive a switch.
 * A hidden tab must stop background work such as polling; it reads this to know whether it is on screen.
 */
export const TabActiveContext = createContext(true)

export function useTabActive(): boolean {
  return useContext(TabActiveContext)
}
