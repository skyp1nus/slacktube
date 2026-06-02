"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";

export function DeleteMappingButton({ id }: { id: string }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  async function del() {
    if (!window.confirm("Delete this mapping?")) return;
    setBusy(true);
    const res = await fetch(`/api/admin/mappings/${id}`, { method: "DELETE" });
    setBusy(false);
    if (res.ok) {
      toast.success("Mapping deleted");
      router.refresh();
    } else {
      toast.error("Failed to delete mapping");
    }
  }

  return (
    <Button variant="outline" size="sm" disabled={busy} onClick={del}>
      Delete
    </Button>
  );
}
