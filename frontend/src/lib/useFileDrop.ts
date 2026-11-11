import { useEffect, useState } from 'react'

/**
 * Accepts files dropped anywhere on the page while the component is mounted.
 * Returns whether files are currently dragged over the window (for a highlight).
 */
export function useFileDrop(onFiles: (files: File[]) => void): boolean {
  const [dragging, setDragging] = useState(false)

  useEffect(() => {
    let depth = 0
    const hasFiles = (e: DragEvent) => e.dataTransfer?.types.includes('Files') ?? false
    const enter = (e: DragEvent) => {
      if (!hasFiles(e)) return
      depth++
      setDragging(true)
    }
    const leave = (e: DragEvent) => {
      if (!hasFiles(e)) return
      depth = Math.max(0, depth - 1)
      if (depth === 0) setDragging(false)
    }
    const over = (e: DragEvent) => {
      if (hasFiles(e)) e.preventDefault() // required to allow dropping
    }
    const drop = (e: DragEvent) => {
      if (!hasFiles(e)) return
      e.preventDefault()
      depth = 0
      setDragging(false)
      const files = [...(e.dataTransfer?.files ?? [])]
      if (files.length > 0) onFiles(files)
    }
    window.addEventListener('dragenter', enter)
    window.addEventListener('dragleave', leave)
    window.addEventListener('dragover', over)
    window.addEventListener('drop', drop)
    return () => {
      window.removeEventListener('dragenter', enter)
      window.removeEventListener('dragleave', leave)
      window.removeEventListener('dragover', over)
      window.removeEventListener('drop', drop)
    }
  }, [onFiles])

  return dragging
}

const ANONYMIZE_KEY = 'breakbar.anonymize'

/** "Spielernamen anonymisieren" is remembered per browser. */
export function useAnonymizeSetting(): [boolean, (value: boolean) => void] {
  const [value, setValue] = useState(() => {
    try {
      return localStorage.getItem(ANONYMIZE_KEY) === 'true'
    } catch {
      return false
    }
  })
  const update = (next: boolean) => {
    setValue(next)
    try {
      localStorage.setItem(ANONYMIZE_KEY, String(next))
    } catch {
      // not persisted, fine
    }
  }
  return [value, update]
}
