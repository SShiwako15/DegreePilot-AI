import {
  BookOpen,
  ClipboardList,
  FileText,
  GraduationCap,
  LayoutDashboard,
  Sparkles,
  Users,
  Settings,
} from "lucide-react";

export type View =
  | "dashboard"
  | "audit"
  | "transcript"
  | "catalog"
  | "planner"
  | "advisor"
  | "advising"
  | "admin";
export const views = [
  { id: "dashboard", label: "Overview", icon: LayoutDashboard },
  { id: "audit", label: "Degree audit", icon: ClipboardList },
  { id: "transcript", label: "My transcript", icon: FileText },
  { id: "catalog", label: "Course catalog", icon: BookOpen },
  { id: "planner", label: "Semester planner", icon: GraduationCap },
  { id: "advisor", label: "AI advisor", icon: Sparkles },
  { id: "advising", label: "Assigned students", icon: Users },
  { id: "admin", label: "Administration", icon: Settings },
] as const;

export function currentView(): View {
  return (
    views.find((view) => `#${view.id}` === location.hash)?.id ?? "dashboard"
  );
}
