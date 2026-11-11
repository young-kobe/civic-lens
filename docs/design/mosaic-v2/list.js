/* Shared per-kind list page: filters, a count that names who and what, 25 rows a page, and an empty state.
   Each page sets window.LIST before it loads this file:
   rows: the records, newest first. match(o,f): true when record o fits form f. what(f): the words after the count.
   row(o): the HTML of one row. With live:true the hash #live shows the page with no records, as the live site is today, and an element #l-zero explains it.
   The filters sit in a fold. It opens on a wide screen, or when the address holds a filter.
   Without JavaScript the form sends a GET request to the same page and the pager links to the next page. */
var CL=(function(){
var MON=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'],ISO=/^\d{4}-\d{2}-\d{2}$/;
function esc(s){return String(s).replace(/[&<>"]/g,function(c){return{'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]})}
function day(iso){var p=iso.split('-');return (+p[2])+' '+MON[+p[1]-1]+' '+p[0]}
function dateOf(f,name){var e=f.elements[name];if(!e)return '';var v=e.value.trim(),bad=v!==''&&!ISO.test(v),h=document.getElementById(e.id+'-err');
if(bad)e.setAttribute('aria-invalid','true');else e.removeAttribute('aria-invalid');if(h)h.textContent=bad?'Enter the date as year, month and day, for example 2026-09-01.':'';return bad?'':v}
function inRange(iso,f){var a=dateOf(f,'from'),b=dateOf(f,'to');return(!iso&&!a&&!b)||(!!iso&&(!a||iso>=a)&&(!b||iso<=b))}
function range(f,verb){var a=dateOf(f,'from'),b=dateOf(f,'to'),v=verb?' '+verb:'';if(a&&b)return v+' from '+day(a)+' to '+day(b);if(a)return v+' on or after '+day(a);if(b)return v+' on or before '+day(b);return ''}
function has(list,v){return !v||list.indexOf(v)>=0}
function words(v,text){return !v||text.toLowerCase().indexOf(v.trim().toLowerCase())>=0}
function tally(title,y,n,p,nv){var t=y+n+p+nv,x=0;function seg(c,k){var w=k/t*100,r='<rect class="'+c+'" x="'+x.toFixed(2)+'%" width="'+w.toFixed(2)+'%" height="100%"/>';x+=w;return k?r:''}
var lg='<li><span class="sw y"></span><b>'+y+'</b> Yes</li><li><span class="sw n"></span><b>'+n+'</b> No</li>'+(p?'<li><span class="sw nv"></span><b>'+p+'</b> present</li>':'')+'<li><span class="sw nv"></span><b>'+nv+'</b> not voting</li>';
return '<div class="row-tally"><svg class="tally" role="img" aria-label="'+esc(title)+': '+y+' Yes, '+n+' No, '+(p?p+' present, ':'')+nv+' not voting">'+seg('y',y)+seg('n',n)+seg('nv',p+nv)+'</svg><ul class="legend">'+lg+'</ul></div>'}
return{esc:esc,day:day,inRange:inRange,range:range,has:has,words:words,tally:tally}})();

(function(){
var L=window.LIST,f=document.getElementById('l-form'),ul=document.getElementById('l-list'),info=document.getElementById('l-info'),none=document.getElementById('l-none'),
pg=document.getElementById('l-page'),zero=document.getElementById('l-zero'),bar=document.getElementById('l-pager'),note=document.querySelector('#l-list').previousElementSibling,pv=document.getElementById('l-prev'),nx=document.getElementById('l-next'),PS=25,page=1;
if(!L||!f)return;
var box=f.closest('details')||f;
var rows=L.live&&location.hash==='#live'?[]:L.rows,base=location.pathname.split('/').pop();
function fields(){return Array.prototype.filter.call(f.elements,function(e){return e.name})}
function read(){var q=new URLSearchParams(location.search);fields().forEach(function(e){if(q.has(e.name))e.value=q.get(e.name)});page=Math.max(1,parseInt(q.get('page'),10)||1)}
function query(p){var q=new URLSearchParams(),s=new URLSearchParams(location.search).get('skin');if(s)q.set('skin',s);fields().forEach(function(e){if(e.value.trim())q.set(e.name,e.value.trim())});if(p>1)q.set('page',p);var t=q.toString();return base+(t?'?'+t:'')}
function pager(a,on,p){a.setAttribute('aria-disabled',String(!on));if(on)a.href=query(p);else a.removeAttribute('href')}
function draw(move){var r=rows.filter(function(o){return L.match(o,f)}),pages=Math.max(1,Math.ceil(r.length/PS));if(page>pages)page=pages;
var a=(page-1)*PS,s=r.slice(a,a+PS),w=L.what(f);ul.innerHTML=s.map(L.row).join('');ul.hidden=bar.hidden=!r.length;if(note)note.hidden=!r.length;none.hidden=r.length>0||!!zero&&!rows.length;
if(zero){zero.hidden=rows.length>0;box.hidden=ul.closest('section').hidden=!rows.length}
info.textContent=r.length?'Showing '+(a+1)+' to '+(a+s.length)+' of '+r.length+' '+w+'.':'0 '+w+'.';
pg.textContent='Page '+page+' of '+pages;pager(pv,page>1,page-1);pager(nx,page<pages,page+1);
history.replaceState(null,'',query(page)+location.hash);if(move)info.scrollIntoView({block:'start'})}
f.addEventListener('submit',function(ev){ev.preventDefault();page=1;draw()});
f.addEventListener('change',function(){page=1;draw()});
f.addEventListener('input',function(ev){if(ev.target.type==='search'){page=1;draw()}});
f.addEventListener('reset',function(){setTimeout(function(){page=1;draw()},0)});
pv.addEventListener('click',function(ev){ev.preventDefault();if(page>1){page--;draw(1)}});
nx.addEventListener('click',function(ev){ev.preventDefault();if(nx.getAttribute('aria-disabled')!=='true'){page++;draw(1)}});
window.addEventListener('hashchange',function(){rows=L.live&&location.hash==='#live'?[]:L.rows;page=1;draw()});
read();if(box.tagName==='DETAILS'&&(matchMedia('(min-width:720px)').matches||fields().some(function(e){return e.value})))box.open=true;draw();})();
