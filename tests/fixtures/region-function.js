const GLOBAL_COUNTRIES = ["HK", "MO", "AU"];
function getIpRegionByCountry(ipCountry) {
  if (!ipCountry) return 1;
  const upper = ipCountry.toUpperCase();
  if (upper === "CN") return 1;
  if (GLOBAL_COUNTRIES.includes(upper)) return 2;
  return 0;
}
