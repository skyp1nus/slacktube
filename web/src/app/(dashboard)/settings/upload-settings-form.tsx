"use client";

import { useEffect, useState } from "react";

import { useSettings, useUpdateSettings } from "@/hooks/use-settings";
import type { Visibility } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";

const VISIBILITY_OPTIONS: { value: Visibility; label: string; hint: string }[] = [
  { value: "private", label: "Private", hint: "only you, in YouTube Studio" },
  { value: "unlisted", label: "Unlisted", hint: "anyone with the link" },
  { value: "public", label: "Public", hint: "listed + searchable" },
];

export function UploadSettingsForm() {
  const { data, isPending } = useSettings();
  const update = useUpdateSettings();

  const [visibility, setVisibility] = useState<Visibility>("private");
  const [chunkMb, setChunkMb] = useState(64);
  const [madeForKids, setMadeForKids] = useState(false);
  const [containsSyntheticMedia, setContainsSyntheticMedia] = useState(false);

  useEffect(() => {
    if (data) {
      setVisibility(data.defaultVisibility);
      setChunkMb(data.transferChunkSizeMb);
      setMadeForKids(data.madeForKids);
      setContainsSyntheticMedia(data.containsSyntheticMedia);
    }
  }, [data]);

  const dirty = data
    ? visibility !== data.defaultVisibility ||
      chunkMb !== data.transferChunkSizeMb ||
      madeForKids !== data.madeForKids ||
      containsSyntheticMedia !== data.containsSyntheticMedia
    : false;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Upload defaults</CardTitle>
        <CardDescription>Applied to every new YouTube upload.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-5">
        {isPending ? (
          <div className="space-y-3">
            <Skeleton className="h-9 w-full max-w-xs" />
            <Skeleton className="h-9 w-full max-w-xs" />
          </div>
        ) : (
          <>
            <div className="space-y-1.5">
              <Label htmlFor="visibility">Visibility</Label>
              <Select value={visibility} onValueChange={(v) => setVisibility(v as Visibility)}>
                <SelectTrigger id="visibility" className="w-full max-w-xs">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {VISIBILITY_OPTIONS.map((o) => (
                    <SelectItem key={o.value} value={o.value}>
                      {o.label} <span className="text-muted-foreground">— {o.hint}</span>
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="chunk">Transfer chunk size (MB)</Label>
              <Input
                id="chunk"
                type="number"
                min={1}
                max={1024}
                value={chunkMb}
                onChange={(e) => setChunkMb(Math.max(1, Math.min(1024, Number(e.target.value) || 1)))}
                className="w-full max-w-xs"
              />
              <p className="text-xs text-muted-foreground">
                Bigger = fewer round-trips on large files (more RAM per chunk). Download is capped at the API max.
              </p>
            </div>

            <div className="flex items-start justify-between gap-4 max-w-md">
              <div className="space-y-0.5">
                <Label htmlFor="madeForKids">Made for kids</Label>
                <p className="text-xs text-muted-foreground">
                  COPPA self-declaration. Off = &ldquo;No, it&rsquo;s not made for kids&rdquo;.
                </p>
              </div>
              <Switch id="madeForKids" checked={madeForKids} onCheckedChange={setMadeForKids} />
            </div>

            <div className="flex items-start justify-between gap-4 max-w-md">
              <div className="space-y-0.5">
                <Label htmlFor="synthetic">Altered or synthetic content (AI)</Label>
                <p className="text-xs text-muted-foreground">
                  Discloses realistic AI-generated / altered media. Off = &ldquo;No&rdquo;.
                </p>
              </div>
              <Switch
                id="synthetic"
                checked={containsSyntheticMedia}
                onCheckedChange={setContainsSyntheticMedia}
              />
            </div>

            <Button
              onClick={() =>
                update.mutate({
                  defaultVisibility: visibility,
                  transferChunkSizeMb: chunkMb,
                  madeForKids,
                  containsSyntheticMedia,
                })
              }
              disabled={!dirty || update.isPending}
            >
              {update.isPending ? "Saving…" : "Save"}
            </Button>
          </>
        )}
      </CardContent>
    </Card>
  );
}
