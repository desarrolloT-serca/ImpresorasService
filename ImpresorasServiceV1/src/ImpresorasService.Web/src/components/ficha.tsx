import * as React from "react"

import { cn } from "@/lib/utils"

// Contenedor de cada pestaña del escritorio: ocupa todo el panel y hace scroll propio.
export function Panel({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      className={cn(
        "flex h-full min-h-0 flex-col gap-4 overflow-auto p-4",
        className
      )}
      {...props}
    />
  )
}
