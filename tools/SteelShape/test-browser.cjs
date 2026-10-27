'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const {chromium}=require('playwright');
(async()=>{
  const target=path.resolve(process.argv[2]||path.join(__dirname,'Steel-weight-calculators.html'));
  const output=path.join(__dirname,'previews');fs.mkdirSync(output,{recursive:true});
  const browser=await chromium.launch({executablePath:process.env.STEEL_TEST_CHROMIUM||'/usr/bin/chromium',headless:true,args:['--no-sandbox','--disable-dev-shm-usage','--disable-background-networking']});
  let checks=0;const errors=[];const check=(name,action)=>Promise.resolve(action()).then(()=>{checks++;console.log('PASS '+name);});
  try{
    const context=await browser.newContext({viewport:{width:1440,height:1000},offline:true});const page=await context.newPage();page.on('pageerror',error=>errors.push(error.message));
    await page.setContent(fs.readFileSync(target,'utf8'));
    await check('offline page starts with retained RHS result',async()=>{assert.equal(await page.locator('#total').innerText(),'39.97');assert.equal(await page.locator('.navigation h2').innerText(),'Steel Shape');assert.ok(!(await page.locator('main').innerText()).includes('Engineering Dashboard'));});
    const section=await page.locator('#section-drawing').innerHTML(),member=await page.locator('#length-drawing').innerHTML();
    await page.locator('#weld-enabled').check();await page.locator('#weld-z').fill('4');
    await check('real browser enables interactive weld without changing main drawings',async()=>{assert.equal(await page.locator('#weld-lengthMm').inputValue(),'292.27');assert.equal(await page.locator('#section-drawing').innerHTML(),section);assert.equal(await page.locator('#length-drawing').innerHTML(),member);});
    async function clickPath(id){
      const hit=page.locator('[data-weld-edge="'+id+'"] path[pointer-events="stroke"]');await hit.scrollIntoViewIfNeeded();
      const point=await hit.evaluate(element=>{const p=element.getPointAtLength(element.getTotalLength()/2);const q=new DOMPoint(p.x,p.y).matrixTransform(element.getScreenCTM());return{x:q.x,y:q.y};});await page.mouse.click(point.x,point.y);
    }
    await check('pointer click on actual edge toggles the weld',async()=>{await clickPath('edge-0');assert.equal(await page.locator('#weld-lengthMm').inputValue(),'251.27');await clickPath('edge-0');assert.equal(await page.locator('#weld-lengthMm').inputValue(),'292.27');});
    await page.locator('#weld-clear').click();await clickPath('edge-0');
    await check('Clear and one edge restore only the clicked path',async()=>{assert.equal(await page.locator('#weld-lengthMm').inputValue(),'41.00');assert.equal(await page.locator('[data-weld-edge][aria-pressed="true"]').count(),1);});
    await page.locator('#weld-edge-full').uncheck();await page.locator('#weld-edge-length').fill('');await page.locator('#weld-edge-length').pressSequentially('25.5');
    await check('partial typing keeps focus and updates length without dragging',async()=>{assert.equal(await page.locator('#weld-edge-length').inputValue(),'25.5');assert.equal(await page.locator('#weld-lengthMm').inputValue(),'25.50');assert.equal(await page.evaluate(()=>document.activeElement.id),'weld-edge-length');});
    await check('oversized partial keeps diagram and exposes validation',async()=>{await page.locator('#weld-edge-length').fill('42');assert.match(await page.locator('#input-error').innerText(),/cannot exceed/);assert.equal(await page.locator('#total').innerText(),'—');assert.equal(await page.locator('#weld-coverage svg').count(),1);await page.locator('#weld-edge-length').fill('20');});
    await check('keyboard Space toggles the SVG edge with focus retained',async()=>{await page.locator('[data-weld-edge="edge-0"]').focus();await page.keyboard.press('Space');assert.equal(await page.locator('#weld-lengthMm').inputValue(),'0.00');assert.equal(await page.evaluate(()=>document.activeElement.dataset.weldEdge),'edge-0');});
    await check('rounded corner can be clicked on its actual curved stroke',async()=>{await clickPath('edge-1');assert.equal(await page.locator('#weld-lengthMm').inputValue(),'7.07');});
    await page.locator('#weld-edge-full').uncheck();await page.locator('#weld-edge-length').fill('3');
    await page.locator('fieldset.weld-panel').screenshot({path:path.join(output,'browser-weld-desktop.png')});
    await check('L connection keeps the main section/member drawings unchanged',async()=>{await page.locator('#weld-connection').selectOption('L');assert.equal(await page.locator('#section-drawing').innerHTML(),section);assert.equal(await page.locator('#length-drawing').innerHTML(),member);});
    await check('the requested All around button is absent',async()=>{assert.equal(await page.getByRole('button',{name:/all around/i}).count(),0);});
    await check('narrow IPE flange tips and both web faces select the intended edge',async()=>{await page.locator('[data-shape="ipe"]').click();await page.locator('#weld-enabled').check();await page.locator('#weld-z').fill('4');for(const id of ['edge-0','edge-1','edge-3','edge-9','edge-11']){await page.locator('#weld-clear').click();await clickPath(id);assert.equal(await page.locator('#weld-edge-picker').inputValue(),id);assert.equal(await page.locator('[data-weld-edge="'+id+'"]').getAttribute('aria-pressed'),'true');assert.equal(await page.locator('[data-weld-edge][aria-pressed="true"]').count(),1);}});
    await context.close();
    const mobile=await browser.newContext({viewport:{width:390,height:844},isMobile:true,hasTouch:true,offline:true});const small=await mobile.newPage();small.on('pageerror',error=>errors.push(error.message));await small.setContent(fs.readFileSync(target,'utf8'));await small.locator('#weld-enabled').check();await small.locator('#weld-z').fill('4');await small.locator('#weld-clear').click();
    await check('mobile selector and editor fit without horizontal scrolling',async()=>{assert.ok(await small.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth+1));await small.locator('#weld-edge-picker').selectOption('edge-2');await small.locator('#weld-edge-full').check();assert.equal(await small.locator('#weld-lengthMm').inputValue(),'91.00');});
    await small.locator('fieldset.weld-panel').screenshot({path:path.join(output,'browser-weld-mobile.png')});await mobile.close();
    await check('no browser JavaScript errors',()=>assert.deepEqual(errors,[]));
    const summary={checks,status:'passed',browser:await browser.version(),artifactSha256:crypto.createHash('sha256').update(fs.readFileSync(target)).digest('hex'),scope:'Real Chromium, the artifact HTML loaded in memory with networking offline (file URLs blocked by browser policy), pointer clicks on straight/curved strokes and narrow profile edges, keyboard, partial typing/focus, main drawings, and 390px mobile layout.'};
    fs.writeFileSync(path.join(__dirname,'browser-test-results.json'),JSON.stringify(summary,null,2)+'\n');console.log(JSON.stringify(summary,null,2));
  }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
