// Vitest + Angular test ortamı kurulumu.
// 1) Derleyici: DI'nin bazı framework servislerini (ör. HttpClient BrowserXhr) çalışma anında
//    JIT ile derleyebilmesi için gerekli — aksi halde "JIT compilation failed" hatası.
import '@angular/compiler';

// 2) Test ortamı builder tarafından başlatılır (initTestEnvironment). Ancak testler arasında
//    TestBed sıfırlanmadığından ikinci testin configureTestingModule'ü "already instantiated"
//    hatası veriyor. Her testten sonra modülü sıfırlayarak bunu gideriyoruz.
import { TestBed } from '@angular/core/testing';
import { beforeEach } from 'vitest';

// beforeEach (afterEach yerine): bir spec'in afterEach'i hata fırlatıp sıfırlamayı atlasa bile,
// her testin BAŞINDA temiz bir TestBed garanti edilir — "already instantiated" zinciri kırılır.
beforeEach(() => {
  TestBed.resetTestingModule();
});
