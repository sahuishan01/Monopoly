import * as THREE from '../vendor/three.module.min.js';

export function tileMaterial(color) { return new THREE.MeshStandardMaterial({ color }); }
export function tokenMaterial(id) { return new THREE.MeshStandardMaterial({ color: new THREE.Color().setHSL((id * 0.17) % 1, 0.7, 0.55) }); }
export function buildingMaterial(prop) {
  return new THREE.MeshStandardMaterial({
    color: prop.Owner >= 0 ? new THREE.Color().setHSL((prop.Owner * 0.17) % 1, 0.6, 0.5) : 0x555555,
    transparent: prop.Mortgaged, opacity: prop.Mortgaged ? 0.4 : 1 });
}
