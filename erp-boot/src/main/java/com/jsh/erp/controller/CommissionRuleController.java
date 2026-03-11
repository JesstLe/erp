package com.jsh.erp.controller;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.base.BaseController;
import com.jsh.erp.datasource.entities.CommissionRule;
import com.jsh.erp.datasource.entities.User;
import com.jsh.erp.service.UserService;
import com.jsh.erp.service.cashier.CommissionRuleService;
import com.jsh.erp.utils.BaseResponseInfo;
import io.swagger.annotations.Api;
import io.swagger.annotations.ApiOperation;
import org.springframework.web.bind.annotation.*;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.util.List;

@RestController
@RequestMapping(value = "/cashier/commissionRule")
@Api(tags = {"提成规则"})
public class CommissionRuleController extends BaseController {
    @Resource
    private CommissionRuleService commissionRuleService;

    @Resource
    private UserService userService;

    @GetMapping(value = "/listEnabled")
    @ApiOperation(value = "获取启用的提成规则")
    public BaseResponseInfo listEnabled(HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            List<CommissionRule> list = commissionRuleService.listEnabled(tenantId);
            res.code = 200;
            res.data = list;
        } catch (Exception e) {
            res.code = 500;
            res.data = "获取数据失败";
        }
        return res;
    }

    @PostMapping(value = "/add")
    @ApiOperation(value = "新增提成规则")
    public BaseResponseInfo add(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = commissionRuleService.insert(obj, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "新增失败";
        }
        return res;
    }

    @PutMapping(value = "/update")
    @ApiOperation(value = "修改提成规则")
    public BaseResponseInfo update(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = commissionRuleService.update(obj, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "修改失败";
        }
        return res;
    }

    @DeleteMapping(value = "/delete")
    @ApiOperation(value = "删除提成规则")
    public BaseResponseInfo delete(@RequestParam("id") Long id, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = commissionRuleService.delete(id, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "删除失败";
        }
        return res;
    }

    @DeleteMapping(value = "/deleteBatch")
    @ApiOperation(value = "批量删除提成规则")
    public BaseResponseInfo deleteBatch(@RequestParam("ids") String ids, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = commissionRuleService.deleteBatch(ids, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "删除失败";
        }
        return res;
    }

    private Long resolveTenantId(User userInfo) {
        if (userInfo == null) {
            return null;
        }
        if ("admin".equals(userInfo.getLoginName())) {
            return null;
        }
        if (userInfo.getTenantId() == null || userInfo.getTenantId() == 0L) {
            return null;
        }
        return userInfo.getTenantId();
    }
}

