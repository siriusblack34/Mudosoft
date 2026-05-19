SELECT
  COUNT(*) FILTER (WHERE "AgentVersion" = '1.0.0.72' AND "Online") AS v72_online,
  COUNT(*) FILTER (WHERE "Online") AS total_online
FROM "Devices";
