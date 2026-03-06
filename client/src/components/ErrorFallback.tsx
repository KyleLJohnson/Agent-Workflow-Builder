import { AlertCircle, RefreshCw } from "lucide-react";
import type { FallbackProps } from "react-error-boundary";

export default function ErrorFallback({ error, resetErrorBoundary }: FallbackProps) {
  return (
    <div className="flex h-screen w-screen items-center justify-center bg-slate-900">
      <div className="max-w-md text-center p-8">
        <AlertCircle size={48} className="text-red-400 mx-auto mb-4" />
        <h1 className="text-xl font-bold text-white mb-2">Something went wrong</h1>
        <p className="text-slate-400 mb-4 text-sm">
          An unexpected error occurred. You can try resetting the application.
        </p>
        <pre className="text-xs text-red-300 bg-slate-800 rounded p-3 mb-4 text-left overflow-auto max-h-32">
          {error instanceof Error ? error.message : String(error)}
        </pre>
        <button
          onClick={resetErrorBoundary}
          className="flex items-center gap-2 mx-auto px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors cursor-pointer"
        >
          <RefreshCw size={16} />
          Reset Application
        </button>
      </div>
    </div>
  );
}
